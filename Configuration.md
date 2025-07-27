# Configuration Reference for GMS-UTML-Patcher

This document describes the configuration options available for patching graphics, fonts, audio, and general import settings.

> [!NOTE]
> All `directory` paths in this configuration are **relative to the location of the JSON patch configuration file**.

---

## Graphics Import Configuration

Used to import and pack game graphics such as sprites and backgrounds.

### Fields

| Field                  | Type                  | Default            | Description                                                                                 |
|------------------------|-----------------------|--------------------|---------------------------------------------------------------------------------------------|
| `directory`            | string **(required)** | —                  | Path to the folder containing graphic files to import                                      |
| `importUnknownAsSprite`| bool                  | `false`            | Whether to treat unknown graphics as sprites                                               |
| `sprFrameRegex`        | string                | `^(.+?)(?:_(\d+))$`| Regex pattern to parse sprite names and frame indices                                      |
| `tempFolder`           | string                | `"graphics-temp/"`  | Temporary folder used during import                                                        |
| `saveTemp`             | bool                  | `false`            | Whether to keep temporary files after import                                               |
| `searchPattern`        | string                | `"*.png"`          | File pattern used to find graphics files                                                  |
| `textureSize`          | int                   | `2048`             | Maximum size for generated atlas textures                                                 |
| `paddingBetweenImages` | int                   | `2`                | Padding in pixels between packed images                                                   |
| `verboseLevel`         | int                   | `0`                | Verbosity level of logging (0 = silent, higher = more verbose)                            |
| `centerOrigin`         | Dictionary            | `{}`               | [Described in Center Origin Settings](#center-origin-settings)                             |
| `changeProps`          | Dictionary            | `{}`               | [Described in `changeProps` — Custom Properties for Sprites](#changeprops--custom-properties-for-sprites) |

**Note:** Properties modifiers are executed in order: **`centerOrigin` → `changeProps`**

### Center Origin Settings

Used to specify sprites whose origin points should be centered horizontally, vertically, or both.

| Field         | Type         | Default | Description                           |
|---------------|--------------|---------|-------------------------------------|
| `centerX`     | string array | `[]`    | List of sprite names to center on X |
| `centerY`     | string array | `[]`    | List of sprite names to center on Y |
| `centerBoth`  | string array | `[]`    | List of sprite names to center both X and Y |

### `changeProps` — Custom Properties for Sprites

This dictionary allows you to define custom properties for specific sprites by their names. These properties override or add settings like size, margin, transparency, and origin point.

#### Supported properties

| Property      | Type       | Description                                                                                 |
|---------------|------------|---------------------------------------------------------------------------------------------|
| `size`        | Object     | Specifies the sprite's width and height in pixels.                                         |
| `margin`      | Object     | Defines margins (left, right, top, bottom) in pixels, used for bounding or collision boxes.|
| `transparent` | Boolean    | Sets the sprite's transparency flag (`true` or `false`).                                   |
| `origin`      | Object     | Specifies the origin point coordinates (`x`, `y`) within the sprite.                       |

#### Example configuration

```json
"changeProps": {
  "spr_spritename": {
    "size": {
      "width": 100,
      "height": 50
    },
    "margin": {
      "left": 10,
      "right": 10,
      "top": 10,
      "bottom": 10
    },
    "transparent": true,
    "origin": {
      "x": 60,
      "y": 25
    }
  }
}
```

---

## Fonts Import Configuration

Similar to graphics import, but for font images.

### Fields

| Field                  | Type                  | Default         | Description                             |
| ---------------------- | --------------------- | --------------- | --------------------------------------- |
| `directory`            | string **(required)** | —               | Folder path containing font image files |
| `tempFolder`           | string                | `"fonts-temp/"` | Temporary folder during import          |
| `saveTemp`             | bool                  | `false`         | Keep temporary files after import       |
| `searchPattern`        | string                | `"*.png"`       | File matching pattern                   |
| `textureSize`          | int                   | `2048`          | Maximum texture size for atlas          |
| `paddingBetweenImages` | int                   | `2`             | Padding between packed images           |
| `verboseLevel`         | int                   | `0`             | Verbosity of logging                    |

---

## Audio Import Configuration

Configures how audio files are imported into GameMaker Studio data.win files. Supports both global settings and per-file overrides.

### Main Configuration Fields

| Field                   | Type    | Default | Description |
|-------------------------|---------|---------|-------------|
| `directory`             | string **(required)** | - | Path to folder containing audio files (relative to patch file) |
| `replace_properties`    | bool    | `false` | Whether to overwrite existing sound properties (volume, pitch, etc.) |
| `embed`                 | bool    | `true` | Embed audio files directly in data.win |
| `decode`                | bool    | `true` | Convert OGG files to PCM format (required for some effects) |
| `use_folder_as_audiogroup` | bool | `false` | Use containing folder name as audio group |
| `manually_configure_each` | bool | `false` | Require individual configuration for each file |
| `files`                 | Dictionary<string, AudioFileConfig> | `null` | Per-file configuration overrides |

### Per-File Configuration

When `manually_configure_each` is `true` or for specific overrides:

| Field        | Type    | Description |
|-------------|---------|-------------|
| `embed`     | bool?   | Override global embed setting |
| `decode`    | bool?   | Override global decode setting |
| `audiogroup`| string? | Specific audio group name |

### Behavior Details

1. **File Processing**:
   - Only `.wav` files are processed (OGG support temporarily disabled)
   - Filename must match existing sound name (e.g. `snd_jump.wav` replaces "snd_jump")

2. **Embedding Rules**:
   - WAV files are always embedded regardless of settings
   - OGG files follow embed/decode flags when enabled

3. **Audio Groups**:
   - New groups are created automatically when needed
   - Default group is used when not specified

### Example Configurations

**Basic Setup** (global settings):
```json
"audio": {
  "directory": "sounds/",
  "embed": true
}
```

**Manual Mode** (explicit config for each file):
```json
"audio": {
  "directory": "audio/",
  "manually_configure_each": true,
  "files": {
    "jump.wav": {
      "embed": true,
      "audiogroup": "gameplay"
    }
  }
}
```

### Important Notes

- When replacing existing sounds, only audio data is updated by default (unless `replace_properties` is true)
- Folder-based audio groups only work when embedding is enabled
