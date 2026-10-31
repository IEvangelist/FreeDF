"""Validates library-produced PAdES/CAdES samples with pyHanko (tested 0.38.0).

1. PDF_SECURITY_OUT=<dir> dotnet test tests/AdoBye.Tests -c Release --filter FullyQualifiedName~Export_pades_samples
2. python validate_pades_with_pyhanko.py <dir>

Trust roots are root.cer and the self-signed *-signer.cer samples; inter.cer is only used for path building. Nothing is fetched.
Files named lib-pades-dss/lta additionally go through pyHanko's LTV validation (PAdES-LT / PAdES-LTA), with revocation data taken
only from the file's /DSS.
"""
import asyncio, glob, os, sys
from pyhanko.keys import load_cert_from_pemder
from pyhanko.pdf_utils.reader import PdfFileReader
from pyhanko.sign.validation import validate_pdf_signature, validate_pdf_timestamp
from pyhanko.sign.validation.ades import ades_lta_validation
from pyhanko.sign.validation.policy_decl import PdfSignatureValidationSpec, SignatureValidationSpec
from pyhanko_certvalidator import ValidationContext
from pyhanko_certvalidator.context import CertValidationPolicySpec
from pyhanko_certvalidator.policy_decl import CertRevTrustPolicy, RevocationCheckingPolicy
from pyhanko_certvalidator.registry import SimpleTrustManager

d = sys.argv[1]
roots = [load_cert_from_pemder(f) for f in glob.glob(os.path.join(d, "*.cer")) if not f.endswith(("inter.cer", "tsa.cer"))]
others = [load_cert_from_pemder(os.path.join(d, n)) for n in ("inter.cer", "tsa.cer") if os.path.exists(os.path.join(d, n))]


def vc():
    return ValidationContext(trust_roots=roots, other_certs=others, allow_fetching=False, revocation_mode="soft-fail")


def ades_spec():
    policy = CertValidationPolicySpec(trust_manager=SimpleTrustManager.build(trust_roots=roots),
                                      revinfo_policy=CertRevTrustPolicy(RevocationCheckingPolicy.from_legacy("hard-fail")))
    return PdfSignatureValidationSpec(SignatureValidationSpec(cert_validation_policy=policy))


for f in sorted(glob.glob(os.path.join(d, "*.pdf"))):
    name = os.path.basename(f)
    try:
        with open(f, "rb") as fh:
            r = PdfFileReader(fh)
            for s in r.embedded_signatures:
                if s.sig_object_type == "/DocTimeStamp":
                    st = validate_pdf_timestamp(s, vc())
                    print(name, s.field_name, "DTS intact=%s valid=%s trusted=%s" % (st.intact, st.valid, st.trusted))
                    continue
                st = validate_pdf_signature(s, vc())
                print(name, s.field_name, "subfilter=%s intact=%s valid=%s trusted=%s docmdp_ok=%s" % (s.sig_object.get("/SubFilter"), st.intact, st.valid, st.trusted, st.docmdp_ok))
                if any(k in name for k in ("pades-dss", "pades-b-lt", "pades-lta")):
                    try:
                        res = asyncio.run(ades_lta_validation(s, ades_spec()))
                        print(name, s.field_name, "ades_lta_validation", "subindication=%s msg=%s" % (res.ades_subindic, res.failure_msg))
                    except Exception as e:
                        print(name, s.field_name, "ades_lta_validation", "ERR", type(e).__name__, str(e)[:300])
    except Exception as e:
        print(name, "ERR", type(e).__name__, str(e)[:200])
