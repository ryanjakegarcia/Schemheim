# Artwork

The banner and package icon use hammer artwork inherited from PlanBuild.
The SVG sources embed the original image and use a flat forest-green background.

The banner lettering uses DejaVu Serif Bold and DejaVu Sans, converted to paths
for consistent rendering. Font notices are in `FONT-LICENSE.txt`.

Render the PNG assets from the repository root with librsvg:

```sh
rsvg-convert assets/artwork/banner.svg -o package/banner.png
rsvg-convert assets/artwork/icon.svg -o package/icon.png
```

or with Inkscape:

```sh
inkscape assets/artwork/banner.svg --export-type=png --export-filename=package/banner.png -w 1200 -h 400
inkscape assets/artwork/icon.svg --export-type=png --export-filename=package/icon.png -w 256 -h 256
```

To change the wordmark, edit the lettering as `<text>` elements and convert
them back to paths with `inkscape --export-text-to-path --export-plain-svg`.
