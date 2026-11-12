package top.mccampfire.core.link.runtime

import java.net.Inet4Address
import java.net.InetAddress

/** The IPv4 loopback address 127.0.0.1. */
val LoopbackV4: Inet4Address = InetAddress.getByAddress(byteArrayOf(127, 0, 0, 1)) as Inet4Address
