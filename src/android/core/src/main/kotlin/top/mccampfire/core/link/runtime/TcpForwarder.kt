package top.mccampfire.core.link.runtime

import java.io.Closeable
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread

/** Accepts TCP connections on a local port and forwards each one to [target] through [connector]. */
class TcpForwarder private constructor(
    private val listener: ServerSocket,
    private val connector: Connector,
    private val target: Inet4Address,
    private val targetPort: Int,
    private val onError: (IOException) -> Unit,
) : Closeable {
    @Volatile
    private var closed = false
    private val sockets = mutableSetOf<Socket>()
    private val active = AtomicInteger()

    /** The local port. */
    val port: Int get() = listener.localPort

    /** Connections being forwarded right now. */
    val activeConnections: Int get() = active.get()

    private fun acceptLoop() {
        while (!closed) {
            val client = try {
                listener.accept()
            } catch (e: IOException) {
                return
            }
            thread(name = "forward", isDaemon = true) { forward(client) }
        }
    }

    private fun forward(client: Socket) {
        val remote = try {
            connector.connect(target, targetPort, ConnectTimeoutMillis)
        } catch (e: IOException) {
            onError(e)
            runCatching { client.close() }
            return
        }
        client.tcpNoDelay = true
        remote.tcpNoDelay = true
        synchronized(sockets) {
            if (closed) {
                client.close()
                remote.close()
                return
            }
            sockets += client
            sockets += remote
        }
        active.incrementAndGet()
        val upstream = thread(name = "forward-up", isDaemon = true) { pump(client.getInputStream(), remote.getOutputStream(), remote) }
        pump(remote.getInputStream(), client.getOutputStream(), client)
        upstream.join()
        active.decrementAndGet()
        synchronized(sockets) {
            sockets -= client
            sockets -= remote
        }
        runCatching { client.close() }
        runCatching { remote.close() }
    }

    private fun pump(from: InputStream, to: OutputStream, toSocket: Socket) {
        val buffer = ByteArray(BufferSize)
        try {
            while (true) {
                val read = from.read(buffer)
                if (read < 0) break
                to.write(buffer, 0, read)
                to.flush()
            }
        } catch (e: IOException) {
            // The connection ended on one side.
        }
        runCatching { toSocket.shutdownOutput() }
    }

    override fun close() {
        closed = true
        runCatching { listener.close() }
        synchronized(sockets) {
            sockets.forEach { runCatching { it.close() } }
            sockets.clear()
        }
    }

    companion object {
        private const val ConnectTimeoutMillis = 10_000
        private const val BufferSize = 16 * 1024

        /**
         * Starts listening on [bindAddress]:[localPort]; throws [IOException] when the port is taken.
         *
         * @param onError Runs on a worker thread when a connection to the target fails.
         */
        fun start(
            localPort: Int,
            connector: Connector,
            target: Inet4Address,
            targetPort: Int,
            bindAddress: InetAddress = LoopbackV4,
            onError: (IOException) -> Unit = {},
        ): TcpForwarder {
            val listener = ServerSocket()
            try {
                listener.bind(InetSocketAddress(bindAddress, localPort))
            } catch (e: IOException) {
                listener.close()
                throw e
            }
            val forwarder = TcpForwarder(listener, connector, target, targetPort, onError)
            thread(name = "forward-accept", isDaemon = true) { forwarder.acceptLoop() }
            return forwarder
        }

        /** True when nothing listens on [port] of [bindAddress]. */
        fun isPortFree(port: Int, bindAddress: InetAddress = LoopbackV4): Boolean =
            runCatching { ServerSocket().use { it.bind(InetSocketAddress(bindAddress, port)) } }.isSuccess

        /** A port nobody listens on right now. */
        fun freePort(): Int = ServerSocket().use {
            it.bind(InetSocketAddress(LoopbackV4, 0))
            it.localPort
        }
    }
}
