"""Regenerates the pyHanko-produced signature fixtures in this directory.

Requires: pyHanko (tested 0.38.0), pikepdf, cryptography. Run:  python generate_pyhanko_fixtures.py
Keys are throw-away and never written to disk; only the signed PDFs and the public certificates are kept.
The tests in SignatureInteropTests.cs read these checked-in files; they do not need Python.
"""
import datetime, io, os
import pikepdf
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID, ExtendedKeyUsageOID
from asn1crypto import x509 as a1x509, keys as a1keys
from pyhanko.sign import signers, fields
from pyhanko.sign.signers.pdf_signer import PdfSigner, PdfSignatureMetadata, PdfTimeStamper
from pyhanko.sign.fields import MDPPerm, SigFieldSpec
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


def make_identity(cn, tsa=False):
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, cn)])
    now = datetime.datetime(2026, 1, 1)
    b = (x509.CertificateBuilder().subject_name(name).issuer_name(name).public_key(key.public_key())
         .serial_number(x509.random_serial_number()).not_valid_before(now).not_valid_after(now + datetime.timedelta(days=3650))
         .add_extension(x509.BasicConstraints(ca=True, path_length=None), critical=True)
         .add_extension(x509.KeyUsage(True, True, False, False, False, True, True, False, False), critical=True))
    if tsa:
        b = b.add_extension(x509.ExtendedKeyUsage([ExtendedKeyUsageOID.TIME_STAMPING]), critical=True)
    cert = b.sign(key, hashes.SHA256())
    ck = a1keys.PrivateKeyInfo.load(key.private_bytes(serialization.Encoding.DER, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
    cc = a1x509.Certificate.load(cert.public_bytes(serialization.Encoding.DER))
    return cert, signers.SimpleSigner(signing_cert=cc, signing_key=ck, cert_registry=SimpleCertificateStore.from_certs([cc])), cc, ck


def base(encrypt=False):
    pdf = pikepdf.Pdf.open(io.BytesIO(RAW))
    out = io.BytesIO()
    if encrypt:
        pdf.save(out, encryption=pikepdf.Encryption(user="u", owner="o", R=6), static_id=True)
    else:
        pdf.save(out, static_id=True)
    return out.getvalue()


def write(name, data):
    with open(os.path.join(HERE, name), "wb") as f:
        f.write(data)


def sign(pdf, signer, field, certify=None, password=None, pades=False, vc=None):
    w = IncrementalPdfFileWriter(io.BytesIO(pdf), strict=False)
    if w.prev.encrypted:
        w.encrypt(password)
    meta = PdfSignatureMetadata(field_name=field, certify=certify is not None, docmdp_permissions=certify or MDPPerm.FILL_FORMS,
                                embed_validation_info=vc is not None, validation_context=vc,
                                subfilter=fields.SigSeedSubFilter.PADES if pades else fields.SigSeedSubFilter.ADOBE_PKCS7_DETACHED)
    return PdfSigner(meta, signer=signer, new_field_spec=SigFieldSpec(field)).sign_pdf(w).getvalue()


cert, signer, cc, ck = make_identity("pyHanko Fixture Signer")
tsa_cert, _, tsa_cc, tsa_ck = make_identity("pyHanko Fixture TSA", tsa=True)
for n, c in (("signer.cer", cert), ("tsa.cer", tsa_cert)):
    write(n, c.public_bytes(serialization.Encoding.DER))

b = base()
write("pyhanko-certified-p1.pdf", sign(b, signer, "Certify", certify=MDPPerm.NO_CHANGES))
p2 = sign(b, signer, "Certify", certify=MDPPerm.FILL_FORMS)
write("pyhanko-certified-p2.pdf", p2)
write("pyhanko-certified-p2-approved.pdf", sign(p2, signer, "Approve"))
write("pyhanko-signed.pdf", sign(b, signer, "Signature1"))
write("pyhanko-encrypted-signed.pdf", sign(base(True), signer, "Signature1", password=b"u"))
write("pyhanko-encrypted-certified-p1.pdf", sign(base(True), signer, "Certify", certify=MDPPerm.NO_CHANGES, password=b"u"))
tsr = DummyTimeStamper(tsa_cert=tsa_cc, tsa_key=tsa_ck)
w = IncrementalPdfFileWriter(io.BytesIO(sign(b, signer, "Signature1")))
write("pyhanko-doctimestamp.pdf", PdfTimeStamper(tsr).timestamp_pdf(w, "sha256", ValidationContext(trust_roots=[tsa_cc], allow_fetching=False)).getvalue())
vc = ValidationContext(trust_roots=[cc], allow_fetching=False, revocation_mode="soft-fail")
write("pyhanko-pades-dss.pdf", sign(b, signer, "Signature1", pades=True, vc=vc))
print("done")
