package idora

const (
	// uuidV7Len is the length of a hyphen-free hexadecimal UUID.
	uuidV7Len = 32

	// uuidV7RandomLen is the number of random octets in a UUIDv7 (octets 6 to 15).
	uuidV7RandomLen = 10

	hexDigits = "0123456789abcdef"
)

// composeUUIDv7 lays out the 16 octets of an RFC 9562 version 7 UUID (spec §3.2): the 48-bit
// timestamp big-endian in octets 0–5, then the random octets with the version nibble forced
// into octet 6 and the variant bits into octet 8.
func composeUUIDv7(ms uint64, random *[uuidV7RandomLen]byte) [16]byte {
	var u [16]byte
	u[0] = byte(ms >> 40)
	u[1] = byte(ms >> 32)
	u[2] = byte(ms >> 24)
	u[3] = byte(ms >> 16)
	u[4] = byte(ms >> 8)
	u[5] = byte(ms)
	copy(u[6:], random[:])
	u[6] = 0x70 | (u[6] & 0x0f)
	u[8] = 0x80 | (u[8] & 0x3f)
	return u
}

// encodeUUIDv7 writes the 32 lowercase hexadecimal characters of a UUIDv7 into dst, which must
// have room for them.
func encodeUUIDv7(dst []byte, ms uint64, random *[uuidV7RandomLen]byte) {
	dst = dst[:uuidV7Len]
	u := composeUUIDv7(ms, random)
	for i, b := range u {
		dst[2*i] = hexDigits[b>>4]
		dst[2*i+1] = hexDigits[b&0x0f]
	}
}
