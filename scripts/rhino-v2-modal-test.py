"""Live v2 test with the separately authorized $10 maximum, never a credit purchase.
No Send or Approve action is automated. Only run on the dedicated test copy.
"""
import os, Rhino
if os.path.basename(os.path.dirname(Rhino.RhinoDoc.ActiveDoc.Path)) != "v2-test-20260907-jjA3vQ":
    raise Exception("Open this run's isolated house-ai.3dm test copy")
demo_budget_limit=10
execfile(os.path.join(os.path.dirname(os.path.abspath(__file__)),"rhino-v1-modal-test.py"),globals())
