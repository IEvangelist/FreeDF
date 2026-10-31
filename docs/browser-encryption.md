# Encrypted PDFs in the browser editor

The browser (.NET WebAssembly) cannot use `System.Security.Cryptography` for AES, so the library carries a pure-managed implementation and picks it automatically.

## Crypto layer

* `PdfCrypto` (Core) is the single entry point used by `PdfDecryptor`, `PdfEncryptor` and the writer for MD5, SHA-1/256/384/512, AES-CBC (PKCS#7 or none), AES-ECB and random bytes.
* On desktop and server it calls the platform primitives (no performance change). When `OperatingSystem.IsBrowser()` is true, or a platform call throws `PlatformNotSupportedException`, it switches to `ManagedCrypto` (`PdfCrypto.UseManaged`).
* `ManagedCrypto` has no reflection or native calls, so it is AOT/trim safe. SHA-2 round constants are derived from prime roots at first use, which keeps the code small. Password and tag comparisons use `CryptographicOperations.FixedTimeEquals`, and PKCS#7 validation does not branch on the padding bytes. AES uses table lookups and is not hardened against cache-timing attacks; the code only runs in a single-user browser tab.
* Randomness stays `RandomNumberGenerator`, which .NET maps to `crypto.getRandomValues` in the browser (verified by the Chromium smoke, which writes an AES-256 file).
* Tests: published vectors (RFC 1321, FIPS 180-4/197, SP 800-38A, RFC 4231), differential tests against the platform over random inputs, Algorithm 2.B parity, cross-provider round trips for every method, samples 17/18 opened with managed crypto, and malformed `/Encrypt` dictionaries.

## Editor behaviour

* Opening an encrypted PDF shows a "Password required" dialog (modal, labelled, masked input, focus trapped, Escape cancels). A wrong password shows "That password is not correct" and asks again. The password is sent once with the open request and is not stored in IndexedDB, localStorage, sessionStorage or logs.
* The editor enforces the `/P` permission flags unless the owner password was used: edits are refused with "This PDF's permissions do not allow you to ...", printing is blocked, text/image export needs the copy flag.
* Save as on a protected document, and Export > "Compressed / password-protected PDF...", open one dialog for the open password, owner password and permission checkboxes and write AES-256 (R6). A non-owner can keep or tighten permissions but never widen them. The owner can choose "Remove protection". Plain Save and Export pages are refused for protected sources so protection is never silently dropped.
* **Recovery policy:** encrypted sources opt out of local recovery (the pill reads "Recovery off (encrypted)"). Neither decrypted bytes nor ciphertext plus an edit log are stored. Storing ciphertext would need a password prompt on resume and a second persisted format; the current choice is the simplest one that cannot leak plaintext.
* Public-key (`Adobe.PubSec`) documents stay unsupported in the browser: they need CMS/RSA decryption with a certificate, which is not available there. The editor says so instead of prompting for a password.
* Merging password-protected files is still unsupported; open the file on its own, remove protection, then merge the copy.
* Size: the crypto layer adds about 13 KB to the unoptimized, untrimmed `PortableDocument.Core.dll` (120,320 to 133,632 bytes in Release); it is plain managed code in an assembly that is already loaded, so there is no extra download request.
