
### open (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-text-300p.pdf | 0.8 | 4.8 (28) | 18.2 (287) | 0.5 (1) | 0.3 (1) | 16.4 (18) |
| syn-incremental-chain-500rev.pdf | 0.9 | 8.7 (32) | 23.0 (262) | 3.0 (3) | 1.7 (2) | 22.8 (23) |
| arxiv-attention.pdf | 2.1 | 4.9 (152) | 10.3 (431) | 0.9 (1) | 0.6 (1) | 3.7 (4) |
| budget2024.pdf | 2.3 | 28.8 (287) | 36.9 (486) | 5.4 (6) | 8.8 (10) | 16.1 (16) |
| 911report.pdf | 2.4 | 21.3 (169) | 24.3 (439) | 0.4 (1) | 2.7 (3) | 28.8 (31) |
| syn-text-cjk-1000p.pdf | 2.8 | 20.1 (39) | 38.7 (261) | 1.4 (2) | 0.6 (1) | 58.4 (57) |
| cfr-title26.pdf | 2.8 | 15.1 (157) | 22.0 (442) | 0.4 (1) | 0.2 (1) | 33.1 (33) |
| irs-p17.pdf | 2.9 | 22.9 (260) | 87.4 (520) | 15.7 (16) | 14.2 (15) | 27.1 (29) |
| usc-title42.pdf | 3.4 | 13.3 (159) | 27.3 (420) | 0.4 (1) | 0.2 (1) | 40.2 (40) |
| syn-text-2000p.pdf | 5.2 | 38.4 (59) | 97.0 (306) | 2.9 (4) | 0.9 (1) | 115.3 (115) |
| syn-text-2000p-rc4-128.pdf | 5.3 | 37.4 (74) | 100.9 (324) | 2.8 (3) | 1.0 (2) | 107.2 (109) |
| syn-text-2000p-aes256.pdf | 5.6 | 40.7 (84) | 94.9 (361) | 5.6 (6) | 21.3 (21) | 129.7 (132) |
| syn-vector-300p.pdf | 7.9 | 3.0 (24) | 7.2 (265) | 0.2 (0) | 0.3 (1) | 11.7 (13) |
| arxiv-llama2.pdf | 13.0 | 3.4 (141) | 19.3 (478) | 0.3 (0) | 0.8 (1) | 6.9 (7) |
| syn-images-40p-3000x2000.pdf | 18.8 | 0.5 (20) | 0.9 (213) | 0.1 (0) | 0.2 (1) | 1.3 (2) |
| darwin-origin-scan.pdf | 24.2 | 20.8 (145) | 28.5 (464) | 0.3 (1) | 0.2 (1) | 22.1 (22) |
| nasa-sp-4029.pdf | 34.4 | 22.1 (311) | 49.4 (517) | 28.6 (31) | 19.8 (23) | 38.3 (39) |
| syn-mixed-objstm-xrefstream.pdf | 58.1 | 315.6 (455) | 344.8 (960) | 4.4 (5) | 4.5 (5) | 272.1 (274) |
| syn-damaged-truncated-tail.pdf | 61.6 | 14262.7 (15141) | ERR | ERR | 224.8 (225) | 1616.7 (1607) |
| syn-mixed-1500p.pdf | 61.6 | 119.0 (119) | 195.8 (384) | 3.9 (4) | 1.1 (2) | 154.2 (155) |
| syn-damaged-bad-startxref.pdf | 61.6 | 15870.7 (15871) | 2129.2 (2521) | 300.2 (309) | 229.7 (230) | 1220.0 (1117) |
| syn-mixed-classic-xref.pdf | 62.4 | 146.2 (163) | 199.3 (403) | 4.5 (5) | 1.1 (2) | 182.3 (164) |

### render (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-damaged-truncated-tail.pdf | 61.6 |  |  | ERR |  |  |

### render_150dpi_per_page (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-text-300p.pdf | 0.8 | 57.3 |  | 5.8 | 3.8 |  |
| syn-incremental-chain-500rev.pdf | 0.9 | 71.0 |  | 5.6 | 7.4 |  |
| arxiv-attention.pdf | 2.1 | 109.0 |  | 11.0 | 11.8 |  |
| budget2024.pdf | 2.3 | 69.5 |  | 8.1 | 6.5 |  |
| 911report.pdf | 2.4 | 54.4 |  | 4.3 | 2.8 |  |
| syn-text-cjk-1000p.pdf | 2.8 | 94.8 |  | 5.7 | 3.3 |  |
| cfr-title26.pdf | 2.8 | 72.5 |  | 6.7 | 4.1 |  |
| irs-p17.pdf | 2.9 | 149.1 |  | 11.8 | 12.1 |  |
| usc-title42.pdf | 3.4 | 63.1 |  | 6.6 | 4.4 |  |
| syn-text-2000p.pdf | 5.2 | 84.5 |  | 5.9 | 2.9 |  |
| syn-text-2000p-rc4-128.pdf | 5.3 | 55.0 |  | 6.7 | 3.0 |  |
| syn-text-2000p-aes256.pdf | 5.6 | 56.0 |  | 5.5 | 3.3 |  |
| syn-vector-300p.pdf | 7.9 | 467.3 |  | 125.6 | 393.4 |  |
| arxiv-llama2.pdf | 13.0 | 114.5 |  | 13.5 | 15.3 |  |
| syn-images-40p-3000x2000.pdf | 18.8 | 119.2 |  | 85.4 | 42.4 |  |
| darwin-origin-scan.pdf | 24.2 | 438.0 |  | 81.0 | 214.5 |  |
| nasa-sp-4029.pdf | 34.4 | 181.2 |  | 120.3 | 79.4 |  |
| syn-mixed-objstm-xrefstream.pdf | 58.1 | 110.1 |  | 6.7 | 5.9 |  |
| syn-damaged-truncated-tail.pdf | 61.6 | 102.2 |  |  | 5.6 |  |
| syn-mixed-1500p.pdf | 61.6 | 122.6 |  | 6.8 | 5.6 |  |
| syn-damaged-bad-startxref.pdf | 61.6 | 105.8 |  | 7.0 | 6.1 |  |
| syn-mixed-classic-xref.pdf | 62.4 | 139.0 |  | 7.4 | 5.8 |  |

### render_72dpi_per_page (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-text-300p.pdf | 0.8 | 48.7 |  | 3.5 | 2.4 |  |
| syn-incremental-chain-500rev.pdf | 0.9 | 47.6 |  | 3.4 | 3.0 |  |
| arxiv-attention.pdf | 2.1 | 86.5 |  | 19.2 | 11.7 |  |
| budget2024.pdf | 2.3 | 72.9 |  | 6.5 | 7.0 |  |
| 911report.pdf | 2.4 | 39.4 |  | 3.3 | 1.6 |  |
| syn-text-cjk-1000p.pdf | 2.8 | 52.0 |  | 4.3 | 1.9 |  |
| cfr-title26.pdf | 2.8 | 65.0 |  | 3.8 | 2.7 |  |
| irs-p17.pdf | 2.9 | 89.1 |  | 8.0 | 9.4 |  |
| usc-title42.pdf | 3.4 | 62.7 |  | 4.8 | 3.3 |  |
| syn-text-2000p.pdf | 5.2 | 48.7 |  | 4.6 | 1.5 |  |
| syn-text-2000p-rc4-128.pdf | 5.3 | 47.9 |  | 5.1 | 1.5 |  |
| syn-text-2000p-aes256.pdf | 5.6 | 46.5 |  | 5.3 | 1.8 |  |
| syn-vector-300p.pdf | 7.9 | 144.3 |  | 44.8 | 147.7 |  |
| arxiv-llama2.pdf | 13.0 | 75.6 |  | 11.2 | 13.6 |  |
| syn-images-40p-3000x2000.pdf | 18.8 | 116.6 |  | 79.2 | 22.9 |  |
| darwin-origin-scan.pdf | 24.2 | 458.5 |  | 54.5 | 201.2 |  |
| nasa-sp-4029.pdf | 34.4 | 194.8 |  | 58.2 | 54.1 |  |
| syn-mixed-objstm-xrefstream.pdf | 58.1 | 39.7 |  | 12.8 | 3.6 |  |
| syn-damaged-truncated-tail.pdf | 61.6 | 39.5 |  |  | 3.5 |  |
| syn-mixed-1500p.pdf | 61.6 | 39.0 |  | 11.2 | 3.6 |  |
| syn-damaged-bad-startxref.pdf | 61.6 | 40.2 |  | 11.4 | 4.1 |  |
| syn-mixed-classic-xref.pdf | 62.4 | 42.6 |  | 11.6 | 4.0 |  |

### save (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-damaged-truncated-tail.pdf | 61.6 |  |  | ERR |  |  |

### save_full (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-text-300p.pdf | 0.8 | 25.3 |  | 22.0 | 61.1 | 44.7 |
| syn-incremental-chain-500rev.pdf | 0.9 | 27.0 |  | 22.1 | 100.9 | 53.8 |
| arxiv-attention.pdf | 2.1 | 106.5 |  | 137.4 | 139.1 | 186.0 |
| budget2024.pdf | 2.3 | 484.1 |  | 1082.5 | 654.0 | 476.5 |
| 911report.pdf | 2.4 | 62.1 |  | 49.9 | 66.3 | 72.4 |
| syn-text-cjk-1000p.pdf | 2.8 | 86.0 |  | 82.6 | 197.9 | 164.8 |
| cfr-title26.pdf | 2.8 | 61.0 |  | 41.8 | 62.4 | 73.7 |
| irs-p17.pdf | 2.9 | 575.8 |  | 1025.6 | 1574.6 | 657.0 |
| usc-title42.pdf | 3.4 | 54.3 |  | 49.7 | 71.7 | 90.3 |
| syn-text-2000p.pdf | 5.2 | 199.3 |  | 160.2 | 357.0 | 320.0 |
| syn-text-2000p-rc4-128.pdf | 5.3 | 260.2 |  | 208.4 | 371.3 | 364.3 |
| syn-text-2000p-aes256.pdf | 5.6 | 311.3 |  | 248.6 | 422.3 | 401.9 |
| syn-vector-300p.pdf | 7.9 | 23.9 |  | 32.2 | 35.9 | 35.9 |
| arxiv-llama2.pdf | 13.0 | 49.2 |  | 73.0 | 82.1 | 1111.3 |
| syn-images-40p-3000x2000.pdf | 18.8 | 25.8 |  | 49.5 | 36.4 | 41.3 |
| darwin-origin-scan.pdf | 24.2 | 87.5 |  | 106.5 | 136.8 | 126.1 |
| nasa-sp-4029.pdf | 34.4 | 941.4 |  | 1658.0 | 2990.0 | 1146.0 |
| syn-mixed-objstm-xrefstream.pdf | 58.1 | 620.8 |  | 967.0 | 798.4 | 764.7 |
| syn-damaged-truncated-tail.pdf | 61.6 | 14575.3 |  |  | 878.9 | 2136.6 |
| syn-mixed-1500p.pdf | 61.6 | 692.3 |  | 971.2 | 758.1 | 1274.9 |
| syn-damaged-bad-startxref.pdf | 61.6 | 15395.4 |  | 1284.1 | 964.3 | 2179.4 |
| syn-mixed-classic-xref.pdf | 62.4 | 695.5 |  | 1068.2 | 813.1 | 1177.2 |

### setup (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-damaged-truncated-tail.pdf | 61.6 |  | ERR |  |  |  |

### text (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-damaged-truncated-tail.pdf | 61.6 |  |  | ERR |  |  |

### text_sampled_pages_with_open (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-text-300p.pdf | 0.8 | 25.5 (109) | 49.4 (133) | 19.2 (21) | 11.4 (16) |  |
| syn-incremental-chain-500rev.pdf | 0.9 | 27.7 (93) | 54.3 (133) | 16.5 (20) | 15.4 (20) |  |
| arxiv-attention.pdf | 2.1 | 160.0 (173) | 265.7 (425) | 61.5 (63) | 31.0 (37) |  |
| budget2024.pdf | 2.3 | 53.6 (114) | 94.5 (252) | 35.5 (38) | 79.2 (102) |  |
| 911report.pdf | 2.4 | 30.2 (77) | 66.0 (172) | 20.8 (23) | 12.7 (18) |  |
| syn-text-cjk-1000p.pdf | 2.8 | 46.8 (144) | 105.2 (289) | 44.6 (51) | 13.5 (19) |  |
| cfr-title26.pdf | 2.8 | 43.7 (106) | 91.5 (234) | 24.0 (27) | 15.8 (21) |  |
| irs-p17.pdf | 2.9 | 131.0 (183) | 236.2 (468) | 69.3 (72) | 74.8 (97) |  |
| usc-title42.pdf | 3.4 | 46.5 (110) | 117.6 (229) | 26.4 (29) | 16.3 (22) |  |
| syn-text-2000p.pdf | 5.2 | 64.2 (135) | 128.5 (227) | 34.2 (39) | 16.7 (23) |  |
| syn-text-2000p-rc4-128.pdf | 5.3 | 66.3 (119) | 164.3 (240) | 34.9 (39) | 17.4 (23) |  |
| syn-text-2000p-aes256.pdf | 5.6 | 57.6 (135) | 149.2 (231) | 41.9 (49) | 38.1 (45) |  |
| syn-vector-300p.pdf | 7.9 | 24.0 (52) | 69.9 (117) | 11.6 (12) | 9.9 (10) |  |
| arxiv-llama2.pdf | 13.0 | 73.3 (181) | 151.2 (292) | 27.5 (32) | 21.6 (28) |  |
| syn-images-40p-3000x2000.pdf | 18.8 | 1.8 (24) | 2.9 (38) | 2.9 (3) | 2.4 (3) |  |
| darwin-origin-scan.pdf | 24.2 | 34.7 (98) | 51.4 (132) | 18.4 (21) | 9.9 (12) |  |
| nasa-sp-4029.pdf | 34.4 | 91.6 (155) | 203.8 (409) | 79.5 (82) | 64.5 (72) |  |
| syn-mixed-objstm-xrefstream.pdf | 58.1 | 272.1 (272) | 400.3 (465) | 93.5 (93) | 156.4 (156) |  |
| syn-damaged-truncated-tail.pdf | 61.6 | 13873.4 (14058) |  |  | 238.0 (240) |  |
| syn-mixed-1500p.pdf | 61.6 | 135.9 (209) | 223.1 (354) | 80.4 (81) | 38.5 (42) |  |
| syn-damaged-bad-startxref.pdf | 61.6 | 14355.2 (14355) | 2141.6 (2402) | 380.8 (384) | 271.6 (253) |  |
| syn-mixed-classic-xref.pdf | 62.4 | 154.6 (208) | 274.0 (357) | 84.3 (87) | 40.9 (43) |  |

### thumb_24dpi_per_page (median ms; `first` in parentheses for open/text; ERR = error)

| file | MB | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|---:|
| syn-text-300p.pdf | 0.8 | 8.6 |  | 1.3 | 4.6 |  |
| syn-incremental-chain-500rev.pdf | 0.9 | 8.1 |  | 1.3 | 5.0 |  |
| arxiv-attention.pdf | 2.1 | 9.8 |  | 4.9 | 7.9 |  |
| budget2024.pdf | 2.3 | 9.7 |  | 2.7 | 4.7 |  |
| 911report.pdf | 2.4 | 6.2 |  | 1.5 | 1.7 |  |
| syn-text-cjk-1000p.pdf | 2.8 | 8.4 |  | 1.4 | 2.6 |  |
| cfr-title26.pdf | 2.8 | 12.0 |  | 1.7 | 2.8 |  |
| irs-p17.pdf | 2.9 | 14.0 |  | 5.2 | 5.0 |  |
| usc-title42.pdf | 3.4 | 10.6 |  | 1.9 | 2.9 |  |
| syn-text-2000p.pdf | 5.2 | 8.6 |  | 1.1 | 1.9 |  |
| syn-text-2000p-rc4-128.pdf | 5.3 | 8.5 |  | 1.1 | 1.9 |  |
| syn-text-2000p-aes256.pdf | 5.6 | 15.1 |  | 1.2 | 2.0 |  |
| syn-vector-300p.pdf | 7.9 | 34.6 |  | 13.2 | 58.9 |  |
| arxiv-llama2.pdf | 13.0 | 10.9 |  | 6.3 | 13.5 |  |
| syn-images-40p-3000x2000.pdf | 18.8 | 111.0 |  | 69.0 | 16.6 |  |
| darwin-origin-scan.pdf | 24.2 | 402.9 |  | 34.3 | 202.5 |  |
| nasa-sp-4029.pdf | 34.4 | 185.2 |  | 45.5 | 47.2 |  |
| syn-mixed-objstm-xrefstream.pdf | 58.1 | 3.9 |  | 1.2 | 2.0 |  |
| syn-damaged-truncated-tail.pdf | 61.6 | 2.9 |  |  | 1.9 |  |
| syn-mixed-1500p.pdf | 61.6 | 4.7 |  | 1.2 | 1.8 |  |
| syn-damaged-bad-startxref.pdf | 61.6 | 3.2 |  | 1.2 | 1.9 |  |
| syn-mixed-classic-xref.pdf | 62.4 | 3.2 |  | 1.4 | 1.9 |  |

### peak working set MB (whole process, all ops)

| file | adobye | pdfpig | pdfium | mupdf | pikepdf |
|---|---:|---:|---:|---:|---:|
| syn-text-300p.pdf | 219 | 68 | 52 | 134 | 35 |
| syn-incremental-chain-500rev.pdf | 212 | 68 | 53 | 134 | 35 |
| arxiv-attention.pdf | 448 | 92 | 78 | 153 | 52 |
| budget2024.pdf | 436 | 116 | 281 | 178 | 86 |
| 911report.pdf | 156 | 76 | 56 | 136 | 43 |
| syn-text-cjk-1000p.pdf | 258 | 95 | 82 | 141 | 52 |
| cfr-title26.pdf | 244 | 75 | 59 | 145 | 45 |
| irs-p17.pdf | 377 | 208 | 216 | 188 | 148 |
| usc-title42.pdf | 249 | 76 | 62 | 145 | 49 |
| syn-text-2000p.pdf | 245 | 93 | 84 | 149 | 69 |
| syn-text-2000p-rc4-128.pdf | 238 | 94 | 82 | 150 | 69 |
| syn-text-2000p-aes256.pdf | 225 | 99 | 84 | 150 | 71 |
| syn-vector-300p.pdf | 798 | 69 | 72 | 149 | 52 |
| arxiv-llama2.pdf | 663 | 122 | 116 | 183 | 72 |
| syn-images-40p-3000x2000.pdf | 736 | 84 | 115 | 232 | 83 |
| darwin-origin-scan.pdf | 959 | 100 | 144 | 296 | 105 |
| nasa-sp-4029.pdf | 1420 | 294 | 441 | 419 | 320 |
| syn-mixed-objstm-xrefstream.pdf | 814 | 291 | 434 | 337 | 321 |
| syn-damaged-truncated-tail.pdf | 738 | 125 | 90 | 351 | 330 |
| syn-mixed-1500p.pdf | 575 | 199 | 432 | 352 | 328 |
| syn-damaged-bad-startxref.pdf | 745 | 216 | 432 | 352 | 329 |
| syn-mixed-classic-xref.pdf | 686 | 200 | 433 | 351 | 331 |

### errors

- syn-damaged-truncated-tail.pdf pdfpig open PdfDocumentFormatException: Could not find an xref trailer or stream dictionary in the input file.
- syn-damaged-truncated-tail.pdf pdfpig setup PdfDocumentFormatException: Could not find an xref trailer or stream dictionary in the input file.
- syn-damaged-truncated-tail.pdf pdfium open PdfiumError: Failed to load document (PDFium: Data format error).
- syn-damaged-truncated-tail.pdf pdfium text PdfiumError: Failed to load document (PDFium: Data format error).
- syn-damaged-truncated-tail.pdf pdfium render PdfiumError: Failed to load document (PDFium: Data format error).
- syn-damaged-truncated-tail.pdf pdfium save PdfiumError: Failed to load document (PDFium: Data format error).
