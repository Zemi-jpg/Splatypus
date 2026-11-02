# Run in a separate visible Rhino instance. Pick all three pairs, then cancel the
# replacement selection. The harness owns and disposes all test geometry.
import os
QUICK_ALIGN_INTERACTIVE = True
execfile(os.path.join(os.path.dirname(__file__), "RunQuickAlignTests.py"))
