"""Native v2 regression on the isolated copy; no AI requests or new AI decisions."""
import os, Rhino
if os.path.basename(os.path.dirname(Rhino.RhinoDoc.ActiveDoc.Path)) != "v2-test-20260907-jjA3vQ":
    raise Exception("Open the dedicated v2 test copy")
drawing_set_version=2
native_create=True
execfile(os.path.join(os.path.dirname(os.path.abspath(__file__)),"rhino-v1-batch-smoke.py"),globals())
