# LANProof Companion Protocol v1

TLS 1.2/1.3 over TCP 13791, selected private IPv4 on Windows. One request/response per connection. The Android client pins the SHA-256 DER leaf certificate fingerprint obtained out of band from the desktop QR. Certificate validity is checked; system-wide trust settings are unchanged.

## Framing

Four-byte **big-endian signed positive** UTF-8 byte length, then JSON bytes. Valid size: 2..262144. Malformed, oversized or timed-out requests close the connection. Server limit: four concurrent connections, ten-second deadline. Client: three-second connect and five-second read timeouts.

Properties: camelCase. Timestamps: Unix UTC milliseconds. Enum names: `Gateway`, `External`, `Dns`, `Http`, `Adapter`.

## Pairing

`lanproof://pair?v=1&host=PRIVATE_IPV4&port=13791&pin=HEX_SHA256&secret=HEX_32_RANDOM_BYTES&expires=UNIX_MS`

The invitation expires after five minutes. The pending UUID and device name appear on Windows for user approval. A different UUID cannot take over that pending request. Approval generates a separate random 256-bit device token. The approved UUID may retrieve it again until an authenticated request proves possession or the invitation expires, allowing lost pairing replies to be retried. The invitation secret is not a short numeric password.

## Requests

Every request contains `version: 1`, `action`, UUID `deviceId`, `deviceName` (1..64 characters) and `credential`. For `pair` the credential is the invitation secret; for `hello`/`sync` it is the device token.

- `pair`: `pending` before approval, `paired` with `token` after approval, `busy` or `unauthorized` otherwise.
- `hello`: authenticated clock exchange; returns `ok` and `serverUtcMs`.
- `sync`: 1..50 `cycles`. Each has positive monotonic `sequence`, `timestampUtcMs`, nullable `clockUncertaintyMs` and exactly five unique observations.

An observation contains `probeType`, `target`, `isSuccess`, `isConclusive`, nullable `roundTripMs`, `jitterMs`, `packetLossPercent`, `dnsResolveMs`, `httpStatusCode` and optional `details`. Numbers must be finite/nonnegative; loss is at most 100. The server assigns `android:UUID` from the authenticated device, ignoring client row IDs. All cycles are validated before any write.

`sync` returns `ok`, `serverUtcMs` and `acknowledgedThrough` after the transaction commits. The client acknowledges only its submitted batch. `(DeviceId, Sequence)` is a durable unique receipt: retrying a lost acknowledgment is safe. Records older than 30 days or over five minutes in the future are rejected. Historical phone anomalies are point observations, not reconstructed outage durations.

## Time

The client brackets an authenticated exchange with elapsed-realtime samples and anchors server time at their midpoint. Initial uncertainty is half the exchange duration plus 5 ms; 100 ppm drift allowance grows with age. Boot count invalidates the anchor after reboot; maximum age is 24 hours. Null uncertainty excludes a result from joint conclusions. Monotonic metadata allows alignment of recent pre-pairing cycles from the same boot.

## Storage and trust boundary

Windows: `data/companion.identity`, current-user DPAPI. Non-Windows test environments use a user-only file (0600). Android: credentials encrypted using Android Keystore AES-GCM; measurements in app-private SQLite. Backup/data transfer is disabled. Revocation clears the server credential and invitation. The protocol exposes no remote commands, file reads or desktop settings changes.

A paired phone is trusted to report its own measurements. A compromised phone can lie about its data, which cannot prove a provider fault. This early protocol has not undergone an independent security audit.
