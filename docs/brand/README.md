# Sela brand kit

Everything here is rendered from the vector logo in `src/Sela/Assets/SelaIcon.xaml`. No rasters are upscaled.

## Files

| File | Use |
| --- | --- |
| `logo/sela-logo-{512,1024,2048,4096}.png` | App tile (rounded square). Transparent outside the tile. The default logo. |
| `logo/sela-mark-{1024,4096}.png` | Just the mark: black notch pill, crescent, green dot. Transparent, no tile. Use it on any plain background. |
| `logo/sela-logo.svg` | Vector version of the tile for web and print. |
| `lockup/sela-lockup-horizontal-dark.png` | Logo + "Sela" wordmark on the night gradient (3000x1000). |
| `lockup/sela-lockup-horizontal-light.png` | Same lockup on `#F7F7F8` with dark text. |
| `social/github-social-preview.png` | GitHub repo social preview (1280x640). Upload in Settings > Social preview. |
| `social/instagram-post-{en,id}.png` | Instagram feed post, 4:5 (1080x1350), English / Indonesian. |
| `social/instagram-story-{en,id}.png` | Instagram story (1080x1920). Text stays within y 250..1450. |
| `social/profile-avatar.png` | Profile picture (1080x1080). Safe for round crops. |
| `social/x-header.png` | X / Twitter header (1500x500). Content stays right of the avatar overlap. |
| `social/linkedin-banner.png` | LinkedIn banner (1584x396). |

## Colors

| Role | Hex |
| --- | --- |
| Background gradient | `#0E1116` → `#1B2230` |
| Notch pill | `#000000` |
| Text | `#F5F5F7` |
| Secondary text | `#9C9CA3` |
| Accent (dot, URL, glow) | `#3DDC97` |
| Crescent gradient | `#8AF5C6` → `#3DDC97` → `#22B477` |
| Light background | `#F7F7F8` |

## Type

- Headlines and wordmark: Segoe UI Variable Display, SemiBold.
- Body: Segoe UI Variable Text.
- Fallback for both: Segoe UI.

Taglines: "A short pause between everything else" (EN) and "Jeda sejenak di sela kesibukan" (ID).

## Usage

- Minimum size: use `sela-logo` at 32 px and up. Below 32 px, use the simplified `SelaLogoSmall` drawing from `SelaIcon.xaml`. It drops the dot, glow and rim.
- Clear space: keep at least 1/4 of the logo width empty on every side.
- Don't recolor the crescent or the dot.
- Don't stretch, skew or rotate the logo.
- Don't place it on busy photos. Use the brand gradient, a plain dark color, or `#F7F7F8`.
- Don't add outlines, extra shadows or effects.

## Regenerate

```powershell
powershell -STA -ExecutionPolicy Bypass -File tools/make-brand-kit.ps1
```

`sela-logo.svg` is a hand translation of the XAML. If the logo geometry changes, update both.
