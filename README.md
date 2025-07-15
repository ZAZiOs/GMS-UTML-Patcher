# GMS-UTML-Patcher

**GMS-UTML-Patcher** is a command-line tool for modifying `data.win` files from GameMaker Studio games using [UndertaleModLib](https://github.com/daeks/UndertaleModLib).
It supports patching game assets and replacing embedded audio with fully configurable behavior.

>[!NOTE]
> **Work in progress**  
> Current limitations:  
> - Not all assets are supported yet  
> - JSON patch file may change in future updates  
> - Documentation is incomplete
>  
> **Want to contribute?** [PRs](https://github.com/ZAZiOs/GMS-UTML-Patcher/pulls) and ideas are welcome in [Issues](https://github.com/ZAZiOs/GMS-UTML-Patcher/issues)! 

## Features

* Loads and validates `data.win` files
* Applies patch instructions from a JSON file
* (Currently) Replaces or adds embedded sounds with optional settings:
  * Embed or link externally
  * Decode `.ogg` or `.wav` to raw audio
  * Assign to audio groups (custom or automatic)
* Optionally generates/updates checksums for data files (`--make-checks`)

## Requirements

* [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

### Dependencies

This project relies on the following libraries:

* [UndertaleModLib](https://github.com/UnderminersTeam/UndertaleModTool)
* [UnderAnalyzer](https://github.com/UnderminersTeam/Underanalyzer)

## Building

```bash
dotnet build
```

## Usage

```bash
GMS-UTML-Patcher --data-path path/to/data.win --patcher-file patch/config.json [options]
```

### Options

| Option                   | Description                                                                                     |
| ------------------------ | ----------------------------------------------------------------------------------------------- |
| `--data-path`            | Path to the original `data.win` file. **(Required)**                                            |
| `--patcher-file`         | Path to the JSON patch configuration. **(Required)**                                            |
| `--output`               | Path for saving the patched file. Defaults to `filename.patched.win`.                               |
| `--skip-hash-check`      | Skips SHA256 hash validation.                                                                   |
| `--skip-timestamp-check` | Skips build timestamp validation AND hash validation                                            |
| `--make-checks`          | Generates or updates checksums in the patch file, without applying the patch.                   |

## Patch Configuration Format

### Audio Patch Example

```json
{
  "audio": {
    "directory": "patch/audio/",
    "replace_properties": true,
    "embed": true,
    "decode": false,
    "use_folder_as_audiogroup": true,
    "manually_configure_each": false
  }
}
```

With per-file overrides:

```json
"files": {
  "snd_click.ogg": {
    "embed": false,
    "decode": false,
    "audiogroup": "group_ui"
  },
  "ambience.wav": {
    "embed": true
  }
}
```

Place your `.ogg` or `.wav` files in the configured `directory`. Filenames must match the sound asset names in the game.

## Exit Codes

Code | Description
-----|-------------------------------
0    | Successfully patched
1    | Unknown error
2    | Argument parsing error
10   | '--data-path' argument not specified
11   | '--patcher-file' argument not specified
20   | Error during checks creation
100  | 'data.win' file not found at the specified path
102  | Failed to load 'data.win'
103  | Patcher file not found at specified path
104  | Failed to save patched data
200  | General error during checks validation
201  | Invalid or missing 'checks' object in test file
202  | Timestamp mismatch
203  | SHA256 mismatch
300  | General patcher error
310  | General Audio Import error
311  | \[AUDIO\] Import folder doesn't exist


## License

This project is licensed under the **MIT License**. See [`LICENSE`](./LICENSE) for details.

>[!NOTE]
> This tool uses [UndertaleModLib](https://github.com/daeks/UndertaleModLib), which is licensed under the **GNU General Public License v3 (GPLv3)**.
> As a result, the final compiled binary is subject to GPLv3 terms.

## Acknowledgments

* [UnderminersTeam](https://github.com/UnderminersTeam) for creating UndertaleModLib
* The GameMaker modding community
