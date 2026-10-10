"""
Regenerates the sample DXF files with ezdxf (pip install ezdxf).
They are written by an independent DXF library on purpose, so the tests
cover real world output rather than only hand written test files.
"""
import math
from pathlib import Path

import ezdxf
from ezdxf.enums import TextEntityAlignment

HERE = Path(__file__).parent


def bracket():
    """L bracket, mm: filleted outline, two holes, one slot, a dimension and a part label."""
    doc = ezdxf.new("R2018", setup=True)
    doc.units = ezdxf.units.MM
    doc.layers.add("CUT")
    doc.layers.add("DIM")
    doc.layers.add("ENGRAVE")
    msp = doc.modelspace()

    # Outline: 120 x 80 L shape, 30 wide legs, 10 mm fillet in the inner corner.
    r = 10
    # The fillet runs clockwise from (40,30) to (30,40): bulge = -tan(90 deg / 4).
    outline = [
        (0, 0, 0), (120, 0, 0), (120, 30, 0), (30 + r, 30, -math.tan(math.radians(90) / 4)),
        (30, 30 + r, 0), (30, 80, 0), (0, 80, 0),
    ]
    msp.add_lwpolyline(outline, format="xyb", close=True, dxfattribs={"layer": "CUT"})

    msp.add_circle((15, 65), 4.25, dxfattribs={"layer": "CUT"})
    msp.add_circle((105, 15), 4.25, dxfattribs={"layer": "CUT"})

    # Slot 30 long, 8 wide, centred at (70, 15)
    msp.add_lwpolyline(
        [(55, 11, 0), (85, 11, 1), (85, 19, 0), (55, 19, 1)],
        format="xyb", close=True, dxfattribs={"layer": "CUT"},
    )

    dim = msp.add_linear_dim(base=(0, -15), p1=(0, 0), p2=(120, 0), dxfattribs={"layer": "DIM"})
    dim.render()

    msp.add_text("BRKT-001", height=4, dxfattribs={"layer": "ENGRAVE"}).set_placement(
        (8, 8), align=TextEntityAlignment.LEFT
    )
    doc.saveas(HERE / "bracket.dxf")


def flange():
    """Round flange, mm: bolt holes placed as rotated block inserts."""
    doc = ezdxf.new("R2018")
    doc.units = ezdxf.units.MM
    msp = doc.modelspace()

    msp.add_circle((0, 0), 75)       # outer diameter 150
    msp.add_circle((0, 0), 30)       # bore 60

    bolt = doc.blocks.new("BOLT_HOLE", base_point=(0, 0))
    bolt.add_circle((55, 0), 6)      # hole 12 on a 110 PCD, drawn at 0 degrees

    for i in range(6):
        msp.add_blockref("BOLT_HOLE", (0, 0), dxfattribs={"rotation": i * 60})

    doc.saveas(HERE / "flange.dxf")


def cover_plate():
    """Cover plate, inches: rounded corners from separate LINE and ARC entities, an ellipse and a spline cutout."""
    doc = ezdxf.new("R2018")
    doc.units = ezdxf.units.IN
    msp = doc.modelspace()

    w, h, r = 6.0, 4.0, 0.5
    msp.add_line((r, 0), (w - r, 0))
    msp.add_arc((w - r, r), r, 270, 0)
    msp.add_line((w, r), (w, h - r))
    msp.add_arc((w - r, h - r), r, 0, 90)
    msp.add_line((w - r, h), (r, h))
    msp.add_arc((r, h - r), r, 90, 180)
    msp.add_line((0, h - r), (0, r))
    msp.add_arc((r, r), r, 180, 270)

    msp.add_ellipse((1.4, 2.0), major_axis=(0.75, 0), ratio=0.5)

    # Organic vent shape: closed spline through fit points
    points = [(3.6 + 1.2 * math.cos(t) * (1 + 0.15 * math.cos(3 * t)),
               2.0 + 1.0 * math.sin(t) * (1 + 0.15 * math.cos(3 * t)))
              for t in [i * 2 * math.pi / 12 for i in range(12)]]
    points.append(points[0])
    msp.add_cad_spline_control_frame(points)

    doc.saveas(HERE / "cover-plate.dxf")


if __name__ == "__main__":
    bracket()
    flange()
    cover_plate()
    print("Samples written to", HERE)
