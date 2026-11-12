package top.mccampfire.core.link

import java.math.BigInteger
import java.security.SecureRandom

/**
 * Invite code of a room, written as `U/XXXX-XXXX-XXXX-XXXX`.
 *
 * The 16 characters are the base-34 digits of a multiple of 7, least significant first. The first eight digits
 * name the virtual network and the last eight are its secret.
 */
class RoomCode private constructor(value: BigInteger) {
    /** The code shown to players. */
    val code: String

    /** Name of the virtual network. */
    val networkName: String

    /** Secret of the virtual network. */
    val networkSecret: String

    init {
        val digits = CharArray(DigitCount)
        var rest = value
        for (i in 0 until DigitCount) {
            val (quotient, remainder) = rest.divideAndRemainder(Base)
            digits[i] = Alphabet[remainder.toInt()]
            rest = quotient
        }
        code = buildString(TextLength) {
            append("U/")
            for (i in 0 until DigitCount) {
                if (i > 0 && i % 4 == 0) append('-')
                append(digits[i])
            }
        }
        networkName = NetworkPrefix + group(digits, 0) + "-" + group(digits, 4)
        networkSecret = group(digits, 8) + "-" + group(digits, 12)
    }

    override fun toString(): String = code

    override fun equals(other: Any?): Boolean = other is RoomCode && other.code == code

    override fun hashCode(): Int = code.hashCode()

    /** The code with its middle groups hidden, for logs. */
    fun masked(): String = code.substring(0, 6) + "-****-****-****"

    companion object {
        private const val Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"
        private const val NetworkPrefix = "scaffolding-mc-"
        private const val DigitCount = 16
        private const val Checksum = 7
        private const val TextLength = 21

        private val Base = BigInteger.valueOf(Alphabet.length.toLong())
        private val Range = Base.pow(DigitCount)
        private val Seven = BigInteger.valueOf(Checksum.toLong())
        private val Random = SecureRandom()

        /** A random code for a new room. */
        fun create(): RoomCode = fromRandom(ByteArray(16).also(Random::nextBytes))

        /** The code made from 16 random bytes read as a little-endian number. */
        fun fromRandom(bytes: ByteArray): RoomCode {
            require(bytes.size == 16) { "16 bytes are required" }
            val value = BigInteger(1, bytes.reversedArray()).mod(Range)
            return RoomCode(value.subtract(value.mod(Seven)))
        }

        /**
         * The first valid code in [text], which may be a whole chat message. Case is ignored and the look-alike
         * letters I and O are read as 1 and 0.
         */
        fun find(text: String?): RoomCode? {
            if (text.isNullOrEmpty()) return null
            val upper = text.uppercase()
            var start = 0
            while (start + TextLength <= upper.length) {
                if (upper[start] == 'U' && upper[start + 1] == '/') {
                    decode(upper, start + 2)?.let { return RoomCode(it) }
                }
                start++
            }
            return null
        }

        private fun decode(text: String, offset: Int): BigInteger? {
            var value = BigInteger.ZERO
            for (i in TextLength - 3 downTo 0) {
                val c = text[offset + i]
                if (i == 4 || i == 9 || i == 14) {
                    if (c != '-') return null
                    continue
                }
                val digit = digitOf(c)
                if (digit < 0) return null
                value = value.multiply(Base).add(BigInteger.valueOf(digit.toLong()))
            }
            return if (value.mod(Seven).signum() == 0) value else null
        }

        private fun digitOf(c: Char): Int = Alphabet.indexOf(
            when (c) {
                'I' -> '1'
                'O' -> '0'
                else -> c
            },
        )

        private fun group(digits: CharArray, start: Int): String = String(digits, start, 4)
    }
}
