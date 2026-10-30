# Signature fixtures

* `pyhanko-*.pdf`, `signer.cer`, `tsa.cer`: produced by **pyHanko 0.38.0** (`python generate_pyhanko_fixtures.py`; needs pikepdf and cryptography). Throw-away self-signed keys, not kept. The timestamp token comes from pyHanko's `DummyTimeStamper` with a local TSA certificate (no real TSA), so it proves structure and imprint handling, not a real authority. `pyhanko-pades-dss.pdf` embeds a self-signed chain with no revocation data (fetching disabled, soft-fail).
* `lib/`: produced by this library (`PDF_SECURITY_OUT=<dir> dotnet test tests/AdoBye.Tests -c Release --filter FullyQualifiedName~Export_docmdp_samples`) and validated with `python validate_with_pyhanko.py <dir>`; pyHanko's verdicts are recorded in `lib/pyhanko-0.38.0-results.txt`.
* `SignatureInteropTests` reads all of these by default; Python is only needed to regenerate.

Known differences: pyHanko reports `modification_level=LTA_UPDATES` rather than `FORM_FILLING` for the encrypted certified sample (its own classification of the update); `docmdp_ok` agrees. pyHanko says its diff policy was "not designed" for DocMDP level 3, though it accepted every P3 sample.
