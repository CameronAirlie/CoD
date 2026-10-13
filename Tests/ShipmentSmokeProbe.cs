using PlutoGE.ScriptCore;
namespace CoD.Scripts;
public sealed class ShipmentSmokeProbe : ScriptBehaviour
{
 [SerializedField] private string resultPath = "";
 private int frames;
 public override void OnUpdate(float dt)
 {
  if (++frames!=15) return;
  var result=resultPath;
  try {
   var nav=GameObject.Find("Navmesh")!;
   var count=0;
   foreach(var spawn in new[]{"North West","North East","South East","South West"})
   foreach(var site in new[]{"Center","Gate","Blue","Forklift","Red"}) {
    var from=GameObject.Find("Spawn "+spawn)!.WorldPosition;
    var to=GameObject.Find("Hardpoint "+site)!.WorldPosition;
    if(!Navigation.FindPath(nav,from,to,.5f,2f).Complete) throw new Exception(spawn+" -> "+site+" unreachable");
    count++;
   }
   if(DedicatedServerSettings.MapFor("Hardpoint","shipment")!="Shipment") throw new Exception("Hardpoint map routing failed");
   File.WriteAllText(result,"PASS: "+count+" spawn-to-hardpoint native navigation paths; dedicated map routing.");
  } catch(Exception ex) {File.WriteAllText(result,"FAIL: "+ex);}
 }
}
