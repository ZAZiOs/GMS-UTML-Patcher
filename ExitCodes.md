# GMS-UTML-Patcher Exit and Error Codes

This document lists all exit and error codes returned by the GMS-UTML-Patcher tool along with their descriptions.

---

## General Codes

| Code | Description                                  |
|-------|---------------------------------------------|
| 0     | Successfully patched                         |
| 1     | Unknown error                               |
| 2     | Argument parsing error                       |
| 10    | `--data-path` argument not specified        |
| 11    | `--patcher-file` argument not specified     |
| 20    | Error during checks creation                 |
| 100   | `data.win` file not found at `--data-path`  |
| 102   | Failed to load `data.win`                    |
| 103   | Patcher file not found at `--patcher-file`  |
| 104   | Failed to save patched data                  |
| 200   | General error during checks validation       |
| 201   | Invalid or missing 'checks' object in test file |
| 202   | Timestamp mismatch                           |
| 203   | SHA256 hash mismatch                         |
| 300   | General patcher error                        |

---

## Audio Import Errors

| Code | Description                                  |
|-------|---------------------------------------------|
| 310   | General Audio Import error                   |
| 311   | Missing "directory" for Audio import         |

---

## Graphics Import Errors

| Code | Description                                  |
|-------|---------------------------------------------|
| 320   | General Graphics import error                |
| 321   | Missing "directory" for Graphics import      |
| 322   | Duplicate file found (X instances)           |
| 323   | Invalid frame index in file X                 |
| 324   | Negative frame index in file X                |
| 325   | Missing frame index X for sprite Y            |

---

## Fonts Import Errors

| Code | Description                                  |
|-------|---------------------------------------------|
| 330   | General Fonts import error                   |
| 331   | Missing "directory" for Fonts import         |
| 332   | Selected folder is empty or contains no images |

---

## GML Import Errors

| Code | Description                                  |
|-------|---------------------------------------------|
| 340   | General GML import error                     |
| 341   | Missing "directory" for GML import           |
| 342   | Selected folder contains no GML files        |
| 343   | Missing modified folder at X                  |
| 344   | Modified folder is empty                      |
| 345   | Missing original folder                       |
| 346   | Unknown import type (only 'base' or 'merge' allowed) |
| 347   | CODE import failed                            |

---

## Notes

- Some codes originate from dependencies or internal libraries.  
- Enable verbose mode for detailed logs to help debug errors.

---

## Support

For help and issues, please open an issue in the repository:  
[https://github.com/ZAZiOs/GMS-UTML-Patcher/issues](https://github.com/ZAZiOs/GMS-UTML-Patcher/issues)
