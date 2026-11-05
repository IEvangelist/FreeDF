import contextlib
import io
import json
import os
import subprocess
import unittest
from unittest.mock import patch

import ua_crosscheck as crosscheck


class CommandBatchTests(unittest.TestCase):
    def test_long_quoted_paths_preserve_order_and_respect_budget(self):
        command = ["dotnet", "run", "--project", "path with spaces", "--"]
        files = [f"path with spaces\\{i}-" + "x" * 250 + ".pdf" for i in range(435)]
        batches = list(crosscheck.command_batches(command, files))
        self.assertGreater(len(batches), 1)
        self.assertEqual(files, [path for batch in batches for path in batch])
        for batch in batches:
            length = len(subprocess.list2cmdline([*command, *batch]).encode("utf-16-le")) // 2
            self.assertLessEqual(length, crosscheck.COMMAND_LINE_LIMIT)

    def test_non_bmp_names_use_utf16_budget(self):
        with patch.object(crosscheck, "COMMAND_LINE_LIMIT", 14):
            self.assertEqual([["\U0001f600" * 3], ["\U0001f600" * 3]],
                             list(crosscheck.command_batches(["d"], ["\U0001f600" * 3] * 2)))

    def test_single_oversized_path_fails(self):
        with self.assertRaisesRegex(SystemExit, "exceeds the command-line budget"):
            list(crosscheck.command_batches(["dotnet"], ["x" * crosscheck.COMMAND_LINE_LIMIT]))

    def test_empty_input(self):
        self.assertEqual([], list(crosscheck.command_batches(["dotnet"], [])))

    def test_subsequent_batches_do_not_build_or_restore(self):
        files = ["a.pdf", "b.pdf"]

        def run(command, **kwargs):
            path = command[-1]
            row = {"file": path, "part": 1, "compliant": True}
            return subprocess.CompletedProcess(command, 0, "build output\n" + json.dumps(row), "")

        with patch.object(crosscheck, "command_batches", return_value=[[files[0]], [files[1]]]), \
                patch.object(crosscheck.subprocess, "run", side_effect=run) as process:
            rows = crosscheck.ours(files)
        self.assertEqual(2, len(rows))
        self.assertNotIn("--no-build", process.call_args_list[0].args[0])
        self.assertNotIn("--no-restore", process.call_args_list[0].args[0])
        self.assertIn("--no-build", process.call_args_list[1].args[0])
        self.assertIn("--no-restore", process.call_args_list[1].args[0])
        self.assertIn((os.path.abspath("b.pdf"), 1), rows)

    def test_failed_later_batch_aborts_instead_of_returning_partial_rows(self):
        first = subprocess.CompletedProcess([], 0, json.dumps({"file": "a.pdf", "part": 1}), "")
        failed = subprocess.CompletedProcess([], 7, "", "failure detail")
        with patch.object(crosscheck, "command_batches", return_value=[["a.pdf"], ["b.pdf"], ["c.pdf"]]), \
                patch.object(crosscheck.subprocess, "run", side_effect=[first, failed]) as process:
            with self.assertRaisesRegex(SystemExit, "batch 2 failed.*refusing to report a partial matrix"):
                crosscheck.ours(["a.pdf", "b.pdf", "c.pdf"])
            self.assertEqual(2, process.call_count)


class RuleOverlapTests(unittest.TestCase):
    mappings = [
        (1, "7.21.4.2-1", "PDFUA-FONT-CHARSET-LIST"),
        (1, "7.21.4.2-2", "PDFUA-FONT-CIDSET-LIST"),
        (1, "7.21.5-1", "PDFUA-FONT-WIDTH"),
        (1, "7.21.7-2", "PDFUA-FONT-TOUNICODE-VALUE"),
        (1, "7.21.8-1", "PDFUA-FONT-NOTDEF"),
        (2, "8.4.5.6-1", "PDFUA-FONT-WIDTH"),
        (2, "8.4.5.8-2", "PDFUA-FONT-TOUNICODE-VALUE"),
        (2, "8.4.5.9-1", "PDFUA-FONT-NOTDEF"),
        (2, "8.7-1", "PDFUA-OC-NAME"),
        (2, "8.4.4-2", "PDFUA-LANG-INVALID"),
        (2, "8.6-1", "PDFUA2-TEXT-STRING-PUA"),
        (1, "7.10-1", "PDFUA-OC-NAME"),
        (1, "7.10-2", "PDFUA-OC-AS"),
        (1, "7.11-1", "PDFUA-FILESPEC-UF"),
        (1, "7.16-1", "PDFUA-ENCRYPT-ACCESS"),
        (1, "7.15-1", "PDFUA-XFA-DYNAMIC"),
        (2, "5-1", "PDFUA-XMP-ID-MISSING"),
        (2, "5-3", "PDFUA-XMP-ID-PREFIX"),
        (2, "8.4.4-1", "PDFUA-LANG"),
        (2, "8.4.5.3.1-1", "PDFUA-FONT-CMAP-SYSTEMINFO"),
        (2, "8.4.5.3.2-1", "PDFUA-FONT-CIDTOGID"),
        (2, "8.4.5.4-1", "PDFUA-FONT-CMAP-EMBEDDED"),
        (2, "8.4.5.5.1-1", "PDFUA-FONT-EMBEDDED"),
        (2, "8.4.5.8-1", "PDFUA-FONT-TOUNICODE"),
        (2, "8.10.2.3-2", "PDFUA2-WIDGET-AA-CONTENTS"),
        (2, "8.10.1-1", "PDFUA-FORM-STRUCT"),
        (2, "8.10.1-2", "PDFUA2-FORM-WIDGETS"),
        (2, "8.10.1-3", "PDFUA2-XFA"),
        (1, "7.20-1", "PDFUA-XOBJECT-REF"),
        (2, "8.2.5.25-1", "PDFUA2-LIST-NUMBERING"),
        (2, "8.2.5.25-2", "PDFUA2-LI-CONTENT"),
        (1, "7.18.8-1", "PDFUA-ANNOT-PRINTERMARK"),
        (2, "8.9.2.4.9-1", "PDFUA2-ANNOT-POPUP"),
        (2, "8.9.2.4.14-1", "PDFUA-ANNOT-PRINTERMARK"),
        (2, "8.9.2.2-1", "PDFUA2-ANNOT-ARTIFACT"),
        (2, "8.9.2.2-2", "PDFUA2-ANNOT-ARTIFACT"),
        (2, "8.10.3.3-1", "PDFUA2-FORM-RV-V"),
        (2, "8.14.1-1", "PDFUA2-FILESPEC-DESC"),
        (2, "8.9.2.4.10-1", "PDFUA2-FILEATTACH-AFRELATIONSHIP"),
    ]

    def overlap(self, part, rule, errors):
        path = os.path.abspath("a.pdf")
        truth = {path: (False, [rule])}
        mine = {(path, part): {"file": path, "part": part, "compliant": False, "errors": errors}}
        output = io.StringIO()
        with patch.object(crosscheck, "collect", return_value=[path]), \
                patch.object(crosscheck, "vera_batch", return_value=truth), \
                patch.object(crosscheck, "ours", return_value=mine), contextlib.redirect_stdout(output):
            crosscheck.corpus("directory", part)
        return output.getvalue()

    def test_delivered_findings_count_as_mapped_family_overlap(self):
        for part, rule, finding in self.mappings:
            with self.subTest(part=part, rule=rule):
                self.assertIn(f"  {rule}: 1 / 1 / 0", self.overlap(part, rule, [finding]))

    def test_incomplete_or_other_findings_are_only_file_verdict_agreement(self):
        for part, rule, finding in self.mappings:
            with self.subTest(part=part, rule=rule):
                self.assertIn(f"  {rule}: 1 / 0 / 1",
                              self.overlap(part, rule, ["PDFUA-FONT-WIDTH-INCOMPLETE", "PDFUA-TITLE"]))

    def test_unimplemented_reference_rule_remains_unmapped(self):
        self.assertIn("  unimplemented-1*: 1 / 0 / 1",
                      self.overlap(2, "unimplemented-1", ["PDFUA-FONT-WIDTH"]))

    def test_incomplete_xfa_is_not_dynamic_rule_overlap(self):
        self.assertIn("  7.15-1: 1 / 0 / 1",
                      self.overlap(1, "7.15-1", ["PDFUA-XFA-INCOMPLETE"]))

    def test_incomplete_font_usage_is_not_embedding_rule_overlap(self):
        for part, rule in [(1, "7.21.4.1-1"), (2, "8.4.5.5.1-1")]:
            with self.subTest(part=part):
                self.assertIn(f"  {rule}: 1 / 0 / 1",
                              self.overlap(part, rule, ["PDFUA-FONT-USAGE-INCOMPLETE", "PDFUA-CONTENT-SCAN-INCOMPLETE"]))


class ProfileSelectionTests(unittest.TestCase):
    def test_default_and_explicit_profiles_are_mutually_exclusive(self):
        xml = '<?xml version="1.0"?><report><jobs/></report>'
        with patch.object(crosscheck.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, xml, "")) as process:
            crosscheck.vera_batch("corpus", 2)
            self.assertIn("ua2", process.call_args.args[0])
            self.assertNotIn("--profile", process.call_args.args[0])
            crosscheck.vera_batch("corpus", 2, "combined profile.xml")
            command = process.call_args.args[0]
            self.assertIn("--profile", command)
            self.assertIn("combined profile.xml", command)
            self.assertNotIn("-f", command)
            self.assertNotIn("ua2", command)

    def test_corpus_cli_preserves_strict_misses_and_profile(self):
        args = ["ua_crosscheck.py", "--strict", "corpus", "directory", "2",
                "--profile", "combined.xml", "--misses"]
        with patch.object(crosscheck.sys, "argv", args), patch.object(crosscheck, "corpus") as corpus:
            crosscheck.main()
        corpus.assert_called_once_with(os.path.abspath("directory"), 2, os.path.abspath("combined.xml"),
                                       misses=True, strict=True)

    def test_missing_profile_value_is_an_argument_error(self):
        with patch.object(crosscheck.sys, "argv", ["tool", "corpus", "directory", "2", "--profile"]), \
                contextlib.redirect_stderr(io.StringIO()), patch.object(crosscheck, "corpus") as corpus:
            with self.assertRaises(SystemExit) as error:
                crosscheck.main()
        self.assertEqual(2, error.exception.code)
        corpus.assert_not_called()

    def test_excluded_file_makes_strict_comparison_fail(self):
        truth = {os.path.abspath("a.pdf"): (True, [])}
        with patch.object(crosscheck, "collect", return_value=["a.pdf"]), \
                patch.object(crosscheck, "vera_batch", return_value=truth), \
                patch.object(crosscheck, "ours", return_value={}), contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit) as error:
                crosscheck.corpus("directory", 2, strict=True)
        self.assertEqual(2, error.exception.code)

    def test_internal_warning_is_not_a_complete_comparison(self):
        row = {"compliant": True, "warnings": ["VALIDATION-INTERNAL"]}
        self.assertIn("incomplete", crosscheck.ours_failure(row))
        self.assertIsNone(crosscheck.ours_failure({"compliant": False, "warnings": []}))

    def test_file_parse_error_keeps_its_diagnostic_and_is_excluded(self):
        path = os.path.abspath("a.pdf")
        truth = {path: (True, [])}
        mine = {(path, None): {"file": path, "error": "PdfParseException: bad xref"}}
        output = io.StringIO()
        with patch.object(crosscheck, "collect", return_value=["a.pdf"]), \
                patch.object(crosscheck, "vera_batch", return_value=truth), \
                patch.object(crosscheck, "ours", return_value=mine), contextlib.redirect_stdout(output):
            crosscheck.corpus("directory", 2)
        self.assertIn("compared=0 excluded=1", output.getvalue())
        self.assertIn("PdfParseException: bad xref", output.getvalue())

    def test_input_omitted_from_reference_report_is_not_lost(self):
        output = io.StringIO()
        with patch.object(crosscheck, "collect", return_value=["a.pdf"]), \
                patch.object(crosscheck, "vera_batch", return_value={}), \
                patch.object(crosscheck, "ours", return_value={}), contextlib.redirect_stdout(output):
            with self.assertRaises(SystemExit) as error:
                crosscheck.corpus("directory", 2, strict=True)
        self.assertEqual(2, error.exception.code)
        self.assertIn("total=1 compared=0 excluded=1", output.getvalue())
        self.assertIn("no report for input", output.getvalue())

    def test_empty_corpus_fails_before_running_reference(self):
        with patch.object(crosscheck, "collect", return_value=[]), \
                patch.object(crosscheck, "vera_batch") as reference:
            with self.assertRaisesRegex(SystemExit, "No PDFs found"):
                crosscheck.corpus("directory", 2, strict=True)
        reference.assert_not_called()

    def test_abnormal_reference_job_is_excluded_even_with_a_verdict(self):
        xml = ('<?xml version="1.0"?><report><jobs><job><item><name>a.pdf</name></item>'
               '<validationReport jobEndStatus="failed" isCompliant="true"/>'
               '</job></jobs></report>')
        with patch.object(crosscheck.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, xml, "")):
            result = crosscheck.vera_batch("directory", 2)
        self.assertIsNone(result[os.path.abspath("a.pdf")][0])


if __name__ == "__main__":
    unittest.main()
