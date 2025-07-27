# GMS-UTML-Patcher

**GMS-UTML-Patcher** is a command-line utility designed to modify `data.win` files created with GameMaker Studio. It uses [UndertaleModLib](https://github.com/daeks/UndertaleModLib) to patch game assets, including graphics, fonts, audio, and GML code.

> [!NOTE]
> **Work in Progress**  
> - Documentation is being improved  
> - Some advanced features may change  
>
> Contributions are welcome! Please see [Issues](https://github.com/ZAZiOs/GMS-UTML-Patcher/issues) and [Pull Requests](https://github.com/ZAZiOs/GMS-UTML-Patcher/pulls).

---

## Features

- Loads and modifies `data.win` files  
- Applies patches based on JSON configuration  
- Supports all major asset types (code, graphics, fonts, audio)  
- Configurable patching behavior for each asset type  
- Checksum verification for data integrity (`--make-checks`)

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)  
- [UndertaleModLib](https://github.com/UnderminersTeam/UndertaleModTool)  

---

## Building

```bash
dotnet build
```
or on Windows:
```powershell
./build.ps1
```

---

## Usage

```bash
GMS-UTML-Patcher --data-path path/to/data.win --patcher-file path/to/patch.json [options]
```

### Command-line options

| Option              | Description                                                                   |
| ------------------- | ----------------------------------------------------------------------------- |
| `--data-path`       | Path to the original `data.win` file (required)                               |
| `--patcher-file`    | Path to the JSON patch configuration (required)                               |
| `--output`          | Output path (default: `original.patched.win`)                                 |
| `--skip-hash-check` | Skip SHA256 hash validation                                                   |
| `--skip-timecheck`  | Skip build timestamp checks                                                  |
| `--make-checks`     | Generate checksums without patching                                          |

---

## Configuration

Basic configuration structure (all sections except `checks` are optional):

```json
{
  "checks": {
    ...
  },
  "gml": {
    "directory": "code/"
  },
  "fonts": {
    "directory": "fonts/"
  },
  "graphics": {
    "directory": "graphics/"
  },
  "audio": {
    "directory": "audio/"
  }
}
```

For detailed configuration options and advanced features, see [Configuration.md](./Configuration.md).

---

## Exit codes

This app has fully documented exit codes, see [ExitCodes.md](./ExitCodes.md)

---

## License

MIT License. See [`LICENSE`](./LICENSE) for details.

> ⚠️ Uses [UndertaleModLib](https://github.com/daeks/UndertaleModLib) (GPLv3).

---

## Support

Report issues at:  
[https://github.com/ZAZiOs/GMS-UTML-Patcher/issues](https://github.com/ZAZiOs/GMS-UTML-Patcher/issues)
