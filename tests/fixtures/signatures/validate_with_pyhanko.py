"""Validates library-produced signature samples with pyHanko (tested 0.38.0).

1. PDF_SECURITY_OUT=<dir> dotnet test tests/FreeDF.Tests -c Release --filter FullyQualifiedName~Export_docmdp_samples
2. python validate_with_pyhanko.py <dir>

Trust roots are the sample certificates themselves; nothing is fetched. For each signature it prints pyHanko's integrity,
validity and DocMDP verdicts so they can be compared with PdfEditor.VerifySignatures.
"""
import glob, os, sys
from pyhanko.keys import load_cert_from_pemder
from pyhanko.pdf_utils.reader import PdfFileReader
from pyhanko.sign.validation import validate_pdf_signature, validate_pdf_timestamp
from pyhanko_certvalidator import ValidationContext

d = sys.argv[1]
roots = [load_cert_from_pemder(os.path.join(d, n)) for n in ("docmdp-signer.cer", "docmdp-tsa.cer")]
for f in sorted(glob.glob(os.path.join(d, "*.pdf"))):
    name = os.path.basename(f)
    try:
        with open(f, "rb") as fh:
            r = PdfFileReader(fh)
            if r.encrypted:
                r.decrypt("u")
            for s in r.embedded_signatures:
                vc = ValidationContext(trust_roots=roots, allow_fetching=False)
                if s.sig_object_type == "/DocTimeStamp":
                    st = validate_pdf_timestamp(s, vc)
                    print(name, s.field_name, "DTS intact=%s valid=%s" % (st.intact, st.valid))
                else:
                    st = validate_pdf_signature(s, vc)
                    print(name, s.field_name, "intact=%s valid=%s docmdp_ok=%s modification_level=%s" % (st.intact, st.valid, st.docmdp_ok, st.modification_level))
    except Exception as e:
        print(name, "ERR", type(e).__name__, str(e)[:200])
