"""Regenerates the pyHanko-produced PAdES fixtures in this directory (pyHanko 0.38.0, cryptography, pikepdf).

    python generate_pades_fixtures.py

Throw-away keys (ECDSA P-256; the TSA key is RSA-2048 because pyHanko's DummyTimeStamper is RSA-only) are generated on every run and never written; only the public root certificate, the PDFs, one CRL and
manifest.json are kept. The PKI is root CA -> signer, TSA. The CRL and OCSP response are issued by the root *before* signing and
embedded by pyHanko (no network: allow_fetching=False). The timestamps come from pyHanko's DummyTimeStamper with a local TSA
certificate, so they prove structure and imprint handling, not a real authority.

Files: pyhanko-pades-b-b.pdf (no validation data), pyhanko-pades-b-lt.pdf (signature timestamp + DSS), pyhanko-pades-b-lta.pdf
(B-LT plus an archive document timestamp), pyhanko-pades-revoked.pdf (signed with a certificate that pyhanko-pades-revoked.crl
lists as revoked one hour after generation; the CRL is synthetic and deliberately post-dates its own thisUpdate to model
"revoked after signing").
manifest.json records the generation instant and the revocation instant.
"""
import datetime, io, json, os
import pikepdf
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec, rsa
from cryptography.x509 import ocsp
from cryptography.x509.oid import NameOID, ExtendedKeyUsageOID
from asn1crypto import x509 as a1x509, keys as a1keys, crl as a1crl, ocsp as a1ocsp
from pyhanko.sign import signers, fields
from pyhanko.sign.signers.pdf_signer import PdfSigner, PdfSignatureMetadata
from pyhanko.sign.fields import SigFieldSpec
from pyhanko.pdf_utils.incremental_writer import IncrementalPdfFileWriter
from pyhanko_certvalidator import ValidationContext
from pyhanko_certvalidator.registry import SimpleCertificateStore
from pyhanko.sign.timestamps.dummy_client import DummyTimeStamper

HERE = os.path.dirname(os.path.abspath(__file__))
RAW = (b"%PDF-1.7\n"
       b"1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>\nendobj\n"
       b"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
       b"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Annots [4 0 R] >>\nendobj\n"
       b"4 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /Rect [10 150 150 190] /P 3 0 R >>\nendobj\n"
       b"trailer\n<< /Root 1 0 R /Size 5 >>\n%%EOF\n")

NOW = datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0)
YEAR = datetime.timedelta(days=365)


def make(cn, issuer=None, issuer_key=None, ca=False, tsa=False):
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048) if tsa else ec.generate_private_key(ec.SECP256R1())
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, cn)])
    b = (x509.CertificateBuilder().subject_name(name).issuer_name(issuer.subject if issuer else name)
         .public_key(key.public_key()).serial_number(x509.random_serial_number())
         .not_valid_before(NOW - datetime.timedelta(days=1)).not_valid_after(NOW + 20 * YEAR)
         .add_extension(x509.BasicConstraints(ca=ca, path_length=0 if ca else None), critical=True)
         .add_extension(x509.SubjectKeyIdentifier.from_public_key(key.public_key()), critical=False))
    if ca:
        b = b.add_extension(x509.KeyUsage(False, False, False, False, False, True, True, False, False), critical=True)
    else:
        b = b.add_extension(x509.KeyUsage(True, True, False, False, False, False, False, False, False), critical=True)
    if tsa:
        b = b.add_extension(x509.ExtendedKeyUsage([ExtendedKeyUsageOID.TIME_STAMPING]), critical=True)
    if issuer is not None:
        b = b.add_extension(x509.AuthorityKeyIdentifier.from_issuer_public_key(issuer_key.public_key()), critical=False)
    cert = b.sign(issuer_key or key, hashes.SHA256())
    return cert, key


def a1cert(c):
    return a1x509.Certificate.load(c.public_bytes(serialization.Encoding.DER))


def a1key(k):
    return a1keys.PrivateKeyInfo.load(k.private_bytes(serialization.Encoding.DER, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))


def make_crl(root, root_key, revoked=()):
    b = (x509.CertificateRevocationListBuilder().issuer_name(root.subject)
         .last_update(NOW - datetime.timedelta(minutes=10)).next_update(NOW + 30 * datetime.timedelta(days=1)))
    for cert, when in revoked:
        b = b.add_revoked_certificate(x509.RevokedCertificateBuilder().serial_number(cert.serial_number).revocation_date(when).build())
    return b.sign(root_key, hashes.SHA256())


def make_ocsp(cert, root, root_key):
    b = (ocsp.OCSPResponseBuilder().add_response(cert=cert, issuer=root, algorithm=hashes.SHA1(), cert_status=ocsp.OCSPCertStatus.GOOD,
                                                 this_update=NOW - datetime.timedelta(minutes=10), next_update=NOW + datetime.timedelta(days=7),
                                                 revocation_time=None, revocation_reason=None)
         .responder_id(ocsp.OCSPResponderEncoding.HASH, root))
    return b.sign(root_key, hashes.SHA256())


root, root_key = make("Fixture Root", ca=True)
signer_cert, signer_key = make("Fixture PAdES Signer", root, root_key)
revoked_cert, revoked_key = make("Fixture Revoked Signer", root, root_key)
tsa_cert, tsa_key = make("Fixture TSA", root, root_key, tsa=True)

root_a1 = a1cert(root)


def signer_for(cert, key):
    cc = a1cert(cert)
    return signers.SimpleSigner(signing_cert=cc, signing_key=a1key(key), cert_registry=SimpleCertificateStore.from_certs([cc, root_a1]))


def vc_for(cert, revoked=()):
    crl = a1crl.CertificateList.load(make_crl(root, root_key, revoked).public_bytes(serialization.Encoding.DER))
    resp = a1ocsp.OCSPResponse.load(make_ocsp(cert, root, root_key).public_bytes(serialization.Encoding.DER))
    return ValidationContext(trust_roots=[root_a1], other_certs=[a1cert(tsa_cert)], crls=[crl], ocsps=[resp] if not revoked else [], allow_fetching=False, revocation_mode="hard-fail")


def base():
    pdf = pikepdf.Pdf.open(io.BytesIO(RAW))
    out = io.BytesIO()
    pdf.save(out, static_id=True)
    return out.getvalue()


def sign(signer, vc=None, ts=False, lta=False):
    w = IncrementalPdfFileWriter(io.BytesIO(base()), strict=False)
    meta = PdfSignatureMetadata(field_name="Signature1", subfilter=fields.SigSeedSubFilter.PADES, embed_validation_info=vc is not None,
                                validation_context=vc, use_pades_lta=lta, md_algorithm="sha256")
    timestamper = DummyTimeStamper(tsa_cert=a1cert(tsa_cert), tsa_key=a1key(tsa_key), certs_to_embed=[a1cert(tsa_cert), root_a1]) if ts else None
    return PdfSigner(meta, signer=signer, new_field_spec=SigFieldSpec("Signature1"), timestamper=timestamper).sign_pdf(w).getvalue()


def write(name, data):
    with open(os.path.join(HERE, name), "wb") as f:
        f.write(data)


write("root.cer", root.public_bytes(serialization.Encoding.DER))
s = signer_for(signer_cert, signer_key)
vc = vc_for(signer_cert)
write("pyhanko-pades-b-b.pdf", sign(s))
write("pyhanko-pades-b-lt.pdf", sign(s, vc, ts=True))
write("pyhanko-pades-b-lta.pdf", sign(s, vc, ts=True, lta=True))
revoke_at = NOW + datetime.timedelta(hours=1)
revoked_vc = ValidationContext(trust_roots=[root_a1], other_certs=[a1cert(tsa_cert)], allow_fetching=False, revocation_mode="soft-fail")
write("pyhanko-pades-revoked.pdf", sign(signer_for(revoked_cert, revoked_key), revoked_vc, ts=True))
write("pyhanko-pades-revoked.crl", make_crl(root, root_key, [(revoked_cert, revoke_at)]).public_bytes(serialization.Encoding.DER))
with open(os.path.join(HERE, "manifest.json"), "w") as f:
    json.dump({"generated": NOW.isoformat(), "revoked_after_signing": revoke_at.isoformat()}, f, indent=1)
print("done")
