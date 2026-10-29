from secure import *

OWNER = "owner123"


def build(path):
    tmp = path + ".plain"
    secure_page("Restricted release", "Encryption  /  Permissions", "OPENS FREELY · RESTRICTED", [
        ("How to open this file", f"No password is needed to read it (empty user password). The owner password is  {OWNER} ; it lifts every restriction."),
        ("Restrictions applied", "Printing is limited to low resolution; copying text, editing, annotating, form filling and page assembly are all disallowed. These are the /P permission bits, enforced cooperatively by the viewer."),
        ("Why it matters", "AES-256 (V5, R6) with an empty user password and a non-empty owner password is the common 'view-only' protection. A reader must derive the file key with the empty password and expose the permission flags to the application."),
    ], tmp)
    perms = pikepdf.Permissions(print_lowres=True, print_highres=False, extract=False, modify_annotation=False,
                                modify_form=False, modify_other=False, modify_assembly=False, accessibility=True)
    encrypt(tmp, path, "", OWNER, perms)


OUT = "18-encrypted-owner-restricted.pdf"
