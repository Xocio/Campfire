package top.mccampfire.core.link.runtime

import java.io.DataInputStream
import java.io.IOException
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket

/** Opens TCP connections to an IPv4 address and port. */
fun interface Connector {
    /** Connects within [timeoutMillis]; throws [IOException] on failure. */
    fun connect(address: Inet4Address, port: Int, timeoutMillis: Int): Socket
}

/** Connects directly. */
object DirectConnector : Connector {
    override fun connect(address: Inet4Address, port: Int, timeoutMillis: Int): Socket {
        val socket = Socket()
        try {
            socket.tcpNoDelay = true
            socket.connect(InetSocketAddress(address, port), timeoutMillis)
            return socket
        } catch (e: IOException) {
            socket.close()
            throw e
        }
    }
}

/** Connects through a SOCKS5 server on this device that needs no authentication. */
class Socks5Connector(private val proxyPort: Int) : Connector {
    override fun connect(address: Inet4Address, port: Int, timeoutMillis: Int): Socket {
        val socket = DirectConnector.connect(LOOPBACK, proxyPort, timeoutMillis)
        try {
            socket.soTimeout = maxOf(timeoutMillis, HandshakeTimeoutMillis)
            Socks5.handshake(socket, address, port)
            socket.soTimeout = 0
            return socket
        } catch (e: IOException) {
            socket.close()
            throw e
        }
    }

    private companion object {
        const val HandshakeTimeoutMillis = 20_000
        val LOOPBACK = InetAddress.getByAddress(byteArrayOf(127, 0, 0, 1)) as Inet4Address
    }
}

/** SOCKS5 CONNECT without authentication. */
object Socks5 {
    private const val Version: Int = 5
    private const val NoAuthentication: Int = 0
    private const val CommandConnect: Int = 1
    private const val AddressIpv4: Int = 1
    private const val AddressDomain: Int = 3
    private const val AddressIpv6: Int = 4

    /** Asks the server behind [socket] to connect to [address]:[port]; throws [IOException] when it refuses. */
    fun handshake(socket: Socket, address: Inet4Address, port: Int) {
        require(port in 1..65535) { "Invalid port $port" }
        val output = socket.getOutputStream()
        val input = DataInputStream(socket.getInputStream())

        output.write(byteArrayOf(Version.toByte(), 1, NoAuthentication.toByte()))
        output.flush()
        if (input.readUnsignedByte() != Version || input.readUnsignedByte() != NoAuthentication) {
            throw IOException("The SOCKS5 server asks for authentication.")
        }

        val request = ByteArray(10)
        request[0] = Version.toByte()
        request[1] = CommandConnect.toByte()
        request[2] = 0
        request[3] = AddressIpv4.toByte()
        address.address.copyInto(request, 4)
        request[8] = (port ushr 8).toByte()
        request[9] = port.toByte()
        output.write(request)
        output.flush()

        if (input.readUnsignedByte() != Version) throw IOException("Not a SOCKS5 server.")
        val reply = input.readUnsignedByte()
        input.readUnsignedByte()
        val skip = when (input.readUnsignedByte()) {
            AddressIpv4 -> 4
            AddressIpv6 -> 16
            AddressDomain -> input.readUnsignedByte()
            else -> throw IOException("Invalid SOCKS5 reply.")
        }
        input.skipBytes(skip + 2)
        if (reply != 0) throw IOException("SOCKS5 connection refused with code $reply.")
    }
}

/** Parses a dotted IPv4 address; returns null for anything else. */
fun parseIpv4(text: String): Inet4Address? {
    val parts = text.split('.')
    if (parts.size != 4) return null
    val bytes = ByteArray(4)
    for (i in 0 until 4) {
        val value = parts[i].toIntOrNull() ?: return null
        if (value !in 0..255 || parts[i].length > 3) return null
        bytes[i] = value.toByte()
    }
    return InetAddress.getByAddress(bytes) as Inet4Address
}
