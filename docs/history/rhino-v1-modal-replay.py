# HISTORICAL ONLY: obsolete v1 fixture; never candidate qualification.
"""Recorded-provider UI regression, explicitly labelled; never contacts an AI service.
Uses the real session, tool executor, staged approval UI and native Rhino executor.
"""
import os,json,uuid,clr,System,Rhino
clr.AddReference("System.Text.Json")
clr.AddReference("RhinoLayoutFoundry.AI.Core")
from System.Text.Json import JsonDocument,JsonElement,JsonDocumentOptions
from System.Threading.Tasks import Task
from RhinoLayoutFoundry.AI.Core import IAiProvider,AiProviderResponse,AiToolCall

class RecordedProvider(IAiProvider):
    def __init__(self): self.phase=0; self.pages=[]; self.capture=0
    def response(self,text,operation=None,args=None):
        calls=[]; items=[]
        if operation:
            callId="fixture-"+uuid.uuid4().hex
            encoded=json.dumps(args)
            calls=[AiToolCall(callId,operation,encoded)]
            raw=json.dumps({"type":"function_call","call_id":callId,"name":operation,"arguments":encoded})
            parsed=JsonDocument.Parse(raw,JsonDocumentOptions())
            items=[parsed.RootElement.Clone()]
            parsed.Dispose()
        return Task.FromResult[AiProviderResponse](AiProviderResponse("recorded",text,
            System.Array[AiToolCall](calls),System.Array[JsonElement](items)))
    def RespondAsync(self,instructions,input,tools,cancellationToken):
        if self.phase==0:
            self.phase=1
            return self.response("RECORDED LOCAL TEST: inspect the document; no external AI request.","inspect_document",{})
        records=[json.loads(unicode(i.GetRawText())) for i in input.Items]
        outputs=[json.loads(i["output"]) for i in records if i.get("type")=="function_call_output"]
        if self.phase==1:
            facts=outputs[-1]["document"]
            root=os.path.dirname(Rhino.RhinoDoc.ActiveDoc.Path)
            with open(os.path.join(root,"..","house-demo-20260906-172458-02de8b","final-verification.json")) as stream:
                golden=json.load(stream)
            def point(v): return dict(zip(("x","y","z"),v))
            cuts=dict((c["viewports"][0],c) for c in golden["clips"])
            sheets=[]
            for i,page in enumerate(sorted(golden["pages"],key=lambda p:p["name"])):
                views=[]
                for j,d in enumerate(page["details"]):
                    cut=cuts.get(d["id"])
                    kind="site_plan" if i==0 else "floor_plan" if i==1 else "section" if i==4 else "elevation"
                    bounds={"left":10,"right":410,"bottom":10,"top":287} if len(page["details"])==1 else {
                        "left":10,"right":410,"bottom":153 if j==0 else 10,"top":287 if j==0 else 144}
                    views.append({"key":"v%d%d"%(i,j),"name":d["name"],"kind":kind,"scale_denominator":200 if i==0 else 100,
                        "bounds_mm":bounds,"camera_location":point(d["camera"]),"camera_target":point(d["target"]),
                        "camera_up":point([0,1,0] if kind in ("site_plan","floor_plan") else [0,0,1]),
                        "cut":None if cut is None else {"origin":point(cut["origin"]),"normal":point(cut["normal"])}})
                sheets.append({"key":"s%d"%i,"name":page["name"],"width_mm":420,"height_mm":297,"views":views})
            args={"schema_version":1,"proposal_id":str(uuid.uuid4()),"document_runtime_serial_number":facts["runtime_serial"],
                "source_revision":facts["revision"],"destination_folder_id":facts["root_folder_id"],"sheets":sheets}
            with open(os.path.join(root,"recorded-proposal.json"),"w") as stream: json.dump(args,stream,indent=2)
            self.phase=2
            return self.response("RECORDED LOCAL TEST: review and approve the five-sheet fixture as one batch.","stage_drawing_set",args)
        if self.phase==2:
            if not outputs[-1].get("succeeded"): return self.response("RECORDED TEST FAILED: batch was not applied.")
            self.phase=3
            return self.response("RECORDED LOCAL TEST: inspect created sheets before captures.","inspect_document",{})
        if self.phase==3:
            self.pages=outputs[-1]["document"]["layouts"]
            if len(self.pages)!=5: return self.response("RECORDED TEST FAILED: expected five sheets.")
            self.phase=4
        if self.capture<len(self.pages):
            page=self.pages[self.capture];self.capture+=1
            return self.response("RECORDED LOCAL TEST: request capture %d/5; human visual review required."%self.capture,
                "capture_layout",{"sheet_page_view_id":page["id"],"width":1024,"height":724})
        return self.response("RECORDED LOCAL TEST COMPLETE: batch approval and all five capture approvals traversed the real modal. No AI interpretation or new AI drawing decisions were performed.")

recorded_provider=RecordedProvider()
execfile(os.path.join(os.path.dirname(os.path.abspath(__file__)),"rhino-v1-modal-test.py"),globals())
