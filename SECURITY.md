# Security Policy

Please report security issues privately through GitHub's security advisory
feature rather than a public issue.

Do not include real device serials, private package identifiers, APKs, signing
material, access tokens, or unredacted device logs in reports.

Treat a Rusty Kiosk direct-link pairing code as a local credential. Rotate it
from the headset after suspected disclosure. Direct protocol v1 authenticates
and integrity-checks requests and responses but does not encrypt HTTP bodies;
use a trusted local network or private hotspot, not an untrusted shared LAN.
