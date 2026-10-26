"""Reproducible quick-start PDF. Uses repository artwork and installed Arial fonts."""
from pathlib import Path
import json
from xml.sax.saxutils import escape
from reportlab.pdfgen import canvas
from reportlab.lib.colors import HexColor, white
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import Paragraph, Frame, Spacer, Table, TableStyle, Image
from reportlab.lib.styles import ParagraphStyle

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
OUT = HERE / 'Splatypus-Quick-Start.pdf'
CONFIG = json.loads((HERE / 'release.json').read_text(encoding='utf-8-sig'))
pdfmetrics.registerFont(TTFont('Arial', 'C:/Windows/Fonts/arial.ttf'))
pdfmetrics.registerFont(TTFont('ArialBold', 'C:/Windows/Fonts/arialbd.ttf'))
pdfmetrics.registerFontFamily('Arial', normal='Arial', bold='ArialBold', italic='Arial', boldItalic='ArialBold')
INK = HexColor('#173D43')
TEAL = HexColor('#398A94')
MUTED = HexColor('#566F73')
PAPER = HexColor('#F5F3ED')
WIDTH, HEIGHT = 595.28, 841.89
OVERVIEW_SIZE = (1190.55, HEIGHT)  # Landscape A3 keeps the diagram labels readable.
body = ParagraphStyle('body', fontName='Arial', fontSize=10.5, leading=15.5, textColor=INK, spaceAfter=10)
h2 = ParagraphStyle('h2', parent=body, fontName='ArialBold', fontSize=15, leading=20, spaceBefore=14, spaceAfter=8)
small = ParagraphStyle('small', parent=body, fontSize=9, leading=13, textColor=MUTED)
cell = ParagraphStyle('cell', parent=body, fontSize=9, leading=13, spaceAfter=0)

def P(text, style=body):
    return Paragraph(text, style)

def section(title, text):
    return [P(title, h2), P(text)]

def table(rows, widths):
    items = [[P(escape(s), cell) for s in row] for row in rows]
    result = Table(items, colWidths=widths, hAlign='LEFT')
    result.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), HexColor('#E2EBE6')),
        ('VALIGN', (0,0), (-1,-1), 'TOP'),
        ('LEFTPADDING', (0,0), (-1,-1), 9), ('RIGHTPADDING', (0,0), (-1,-1), 9),
        ('TOPPADDING', (0,0), (-1,-1), 8), ('BOTTOMPADDING', (0,0), (-1,-1), 8),
        ('LINEBELOW', (0,0), (-1,-1), .5, HexColor('#D8E2DF')),
    ]))
    return result

c = canvas.Canvas(str(OUT), pagesize=(WIDTH, HEIGHT))
c.setTitle('Splatypus - Quick Start')
c.setAuthor(CONFIG['author'])
c.setSubject('Installation and local photogrammetry / Gaussian splat training workflows for Rhino 8')

def page(number, label, title, subtitle, story, pagesize=(WIDTH, HEIGHT)):
    WIDTH, HEIGHT = pagesize
    c.setPageSize(pagesize)
    c.setFillColor(PAPER); c.rect(0,0,WIDTH,HEIGHT,fill=1,stroke=0)
    c.setFillColor(TEAL); c.rect(44,HEIGHT-54,28,4,fill=1,stroke=0)
    c.drawImage(str(HERE / 'assets/splatypus-icon-64.png'), WIDTH-76, HEIGHT-73, 32, 32, mask='auto')
    c.setFillColor(MUTED); c.setFont('ArialBold',9); c.drawString(82,HEIGHT-53,'SPLATYPUS  /  '+label.upper())
    c.setFillColor(INK); c.setFont('ArialBold',29); c.drawString(44,HEIGHT-105,title)
    frame = Frame(44,74,WIDTH-88,HEIGHT-200,leftPadding=0,rightPadding=0,topPadding=0,bottomPadding=0)
    content=[P(subtitle,small),Spacer(1,10)] + story
    frame.addFromList(content,c)
    if content:
        raise RuntimeError(f'Page {number} overflow: {len(content)} unplaced elements')
    c.setStrokeColor(HexColor('#D8E2DF')); c.line(44,54,WIDTH-44,54)
    c.setFont('Arial',8); c.setFillColor(MUTED)
    c.drawString(44,38,'Rhino 8.34+ / .NET 8  |  Updated 11 October 2026')
    c.drawRightString(WIDTH-44,38,f'{number} / 5')
    c.showPage()

page(1,'Overview','From media to models',
     'Splatypus 0.1.0 - photogrammetry and Gaussian splat training workflows for Grasshopper.',[
    *section('A connected local workflow',
       'Splatypus connects FFmpeg media import, COLMAP camera reconstruction and Brush training through one project. '
       'Inspect cameras and points, align the reconstruction, prepare the active dataset, train and inspect PLY exports.'),
    Image(str(HERE / 'assets/splatypus-workflow-horizontal.png'),
          width=OVERVIEW_SIZE[0]-88, height=(OVERVIEW_SIZE[0]-88)*1120/3200),
    Spacer(1,12),
    *section('Know the scope',
        '<b>A Gaussian splat viewport renderer is not included.</b> Splat Preview shows colored points. '
        'The current build focuses on media import, camera reconstruction, alignment and training.'),
    P('Separate Windows x64 and macOS packages are provided. Automatic Mac tool installation targets Apple Silicon. '
      'Consult README.md for package requirements and platform details.',small),
], pagesize=OVERVIEW_SIZE)

page(2,'Install','Install and check',
     'Install through Rhino Package Manager or use the package matching your platform.',[
    *section('Before installing',
       'Use <b>Rhino 8.34.26223.11001 or newer with .NET 8</b> and Grasshopper. Rhino 7 and .NET Framework mode are unsupported. '
       'Choose Package Manager or manual installation and keep one installed copy. '
       'Back up your project media and save working definitions outside the installed package folder.'),
    *section('Install from Rhino',
       'Run <b>_PackageManager</b>, search <b>Splatypus</b>. '
       'Choose the compatible package, install, then restart Rhino. Open Grasshopper, find the Splatypus tab and place '
       '<b>SP Hello</b>. Connect Status to a Panel. Expected: <b>SP is loaded: Ready</b>.'),
    *section('Manual ZIP installation',
       'Find Grasshopper > File > Special Folders > Components Folder. Close Rhino. Unblock the downloaded ZIP on Windows '
       'if the checkbox is present, extract it and copy <b>both Splatypus.gha and Splatypus.Core.dll</b> into a Splatypus '
       'subfolder. Keep one copy and restart. Use the build for your operating system; update both files together.'),
    *section('External tools are a separate step',
       'Use SP Download Tools with a writable Folder (or leave it blank for the per-user default). On Windows choose '
       '<b>CUDA=False</b> for CPU COLMAP or True for NVIDIA CUDA. Press Download with a Button. Wait for Ready=True and '
       'connect the returned FFmpeg, COLMAP and Brush paths to later Exe inputs. Startup checks do not certify GPU training.'),
    table([
       ['Platform','Pinned tool versions'],
       ['Windows x64','FFmpeg + ffprobe 8.0; COLMAP 3.11.1 CPU/CUDA; Brush 0.3.0.'],
       ['Apple Silicon macOS','FFmpeg + ffprobe 7.1.1; COLMAP 3.11.1 CPU through conda-forge; Brush 0.3.0.'],
    ],[150,357]),
    P('Automatic tool installation does not support Intel Macs or Windows ARM. Tools can also be supplied through manual '
      'native executable paths. See README.md, Installation and External tools, for detail.',small),
])

page(3,'First run','Reconstruct the scene',
     'Begin with a small, representative capture that you own or have permission to process.',[
    *section('1. Create the project',
       'Place SP Project. Choose a writable Parent Folder and Project Name, set Create=True and use Root for downstream '
       'components. The project contains images, colmap, brush and output folders. Keep your original media separately.'),
    *section('2. Import media',
       'Connect FFmpeg Exe and Root to SP Media Import. Set source and sampling options. The estimate updates when inputs change. '
       'If files inside a source folder change, use <b>Refresh media estimate</b> in the component right-click menu on Windows, '
       'or a Button on <b>Refresh Estimate</b> on macOS. Then press Run. FFprobe must be beside FFmpeg. Inspect images for blur and redundancy.'),
    *section('3. Run COLMAP',
       'Connect COLMAP Exe and Root to <b>SP COLMAP Pipeline</b>. Extract, Match, Sparse and Export default to True; Undistort '
       'defaults to False. Press Run and follow Status/Progress. Alternatively, run the individual reconstruction components '
       'in sequence. Do not run both approaches simultaneously on the same project.'),
    *section('4. Inspect and optionally refine',
       'Use Model Report, Camera Planes and Sparse Point Cloud. Sparse reconstruction has no automatic real-world scale. '
       'Optional undistortion updates images and calibration together. Alignment writes a separate model with transformed points '
       'and camera poses, preserving the base pair. Connect Aligned Sparse Model to Brush Dataset\'s Sparse Model Override '
       'when you want to train that branch.'),
    *section('One current image/model pair',
       '<b>Root -> SP Active Dataset -> SP Brush Dataset.</b> Re-prepare after reconstruction, undistortion or changing the aligned override. '
       'Do not train while changing the dataset. See README.md, COLMAP and the active dataset, for the complete connection guide.'),
    P('Capture tips: overlap photographs, vary viewpoints, use sharp exposures and retain visible detail. Motion, reflections '
      'and textureless regions are difficult. Exhaustive matching gets expensive as image count grows; more images do not '
      'guarantee a better result.',small),
])

page(4,'Train and inspect','Train, export and preview',
     'Brush is a separate local application with its own graphics-device requirements.',[
    *section('5. Prepare the Brush dataset',
       'Connect Root to SP Active Dataset and its Dataset to SP Brush Dataset. Enable Prepare. Dataset links depend on '
       'the project files remaining available and the filesystem supporting links. Follow any OS link-permission error.'),
    *section('6. Start a short training test',
       'Connect Brush Exe, the prepared Dataset and Root to SP Brush Train. Use a new Job Name to retain earlier exports. '
       'The default is 30,000 steps; start with a short test. Set Viewer=False for a basic background run. Run starts the '
       'job; Cancel stops it. Use a Button or reset a toggle to False before the next Run.'),
    *section('7. Read the outputs',
       'Exports and the full brush-training.log are in <b>output/&lt;job name&gt;</b>. Check Code and Status. '
       '<b>Replace=True removes that job\'s previous exports before training.</b> Completed exports can remain after cancellation.'),
    *section('8. Inspect a PLY',
       'Select an exported binary little-endian PLY in SP Splat Preview. Max defaults to 150,000 points; lower it for faster '
       'interaction. Preview keeps stored coordinates and displays a decimated colored point cloud. It does not rasterize '
       'Gaussian splats.'),
    *section('Optional external rendering handoff',
       'Rotate for Visualization writes an additional <b>_rgs.ply</b> rotated +90 degrees around X for a compatible external '
       'RhinoGaussianSplatting workflow. It preserves the original PLY. Splatypus does not include or license that third-party renderer.'),
    P('If training fails, review the log, verify the selected GPU/driver with the pinned Brush build, reduce the input workload '
      'and test Viewer=False. CPU COLMAP selection does not make Brush a CPU trainer.',small),
])

page(5,'Recovery and notices','Keep the work traceable',
     'Use logs and small reproductions to diagnose problems without exposing private project data.',[
    *section('Common checks',
       '<b>No tab:</b> check Rhino/.NET runtime, both plugin files, platform and duplicate installations. '
       '<b>Run does nothing:</b> reset False or use a Button. <b>Download fails:</b> check Folder permissions and HTTPS access '
       'to GitHub/CDN; Apple Silicon also needs conda-forge. <b>Slow preview:</b> lower Max points.'),
    *section('Logs and cancellation',
       'Set Terminal=True before starting a job for live output where supported. Closing the terminal does not cancel processing; '
       'use Cancel. Review Status, Code and saved logs. Keep independent backups before replacement or refinement.'),
    *section('Local processing and privacy',
       'The inspected Splatypus code has no publisher telemetry, login or automatic project upload. Images/models/logs are local. '
       'Download Tools sends normal network requests to upstream providers only when triggered. Rhino/Food4Rhino and external '
       'tools have their own policies. Logs can disclose usernames, file paths and arguments; redact them before sharing.'),
    *section('Credits and licenses',
       f'<b>Splatypus is licensed under MIT, copyright {CONFIG["copyrightYear"]} {escape(CONFIG["copyrightHolder"])}.</b> FFmpeg licensing depends on the selected build '
       '(LGPL with possible GPL parts). COLMAP itself is BSD-3-Clause; Brush 0.3.0 is Apache-2.0. Dependencies are separately licensed. These tools are downloaded separately and are '
       'not bundled in the plugin package. Host and tool names remain with their owners; no endorsement is implied.'),
    *section('Support and further reading',
       f'Contact <link href="mailto:{escape(CONFIG["supportContact"])}" color="#398A94">{escape(CONFIG["supportContact"])}</link>. '
       f'Website: <link href="{escape(CONFIG["website"])}" color="#398A94">{escape(CONFIG["website"])}</link>. '
       'Include Splatypus/Rhino/.NET/OS versions, CPU/GPU, tool versions, '
       'settings, Status/Code and a redacted log excerpt. See LICENSE.txt and NOTICE.txt for copyright, use terms and the public support contact.'),
    P('README.md contains the full workflow, example setup, troubleshooting, privacy and third-party credits. '
      'Examples/Splatypus_Workflow.gh is the included Grasshopper definition.',small),
    P('Primary references: <link href="https://developer.rhino3d.com/guides/yak/creating-a-grasshopper-plugin-package/" color="#398A94">McNeel packaging</link>; '
      '<link href="https://ffmpeg.org/legal.html" color="#398A94">FFmpeg licensing</link>; '
      '<link href="https://github.com/colmap/colmap/blob/3.11.1/COPYING.txt" color="#398A94">COLMAP license</link>; '
      '<link href="https://github.com/ArthurBrussee/brush/blob/v0.3.0/LICENSE" color="#398A94">Brush license</link>.',small),
])
c.save()
print(f'Created five-page guide: {OUT}')
