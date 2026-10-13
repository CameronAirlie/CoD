using System.Numerics;
using PlutoGE.ScriptCore;
namespace CoD.Scripts;
public sealed partial class MultiplayerSession
{
 internal void CheckTerminal()
 {
  static void Check(bool c,string m){if(!c)throw new Exception(m);}
  Check(navigationMesh?.EntityId==GameObject.Find("Navmesh")?.EntityId,"Session navigation binding incorrect");
  Check(IsDefusal&&navigationMesh is not null&&navigationMesh.IsValid,"Terminal Defusal navigation missing");
  int paths=0;
  foreach(var role in new[]{"Attack","Defend"}) {
   var spawn=GameObject.Find(role=="Attack"?"Attacker Spawn":"Defender Spawn")!.WorldPosition;
   foreach(var lane in new[]{"West","Mid","East"}) {
    var p=GameObject.Find("Defusal "+role+" "+lane)!.WorldPosition;
    var ok0=Navigation.ProjectPoint(navigationMesh!,spawn,out var ps,.5f,2f);var ok1=Navigation.ProjectPoint(navigationMesh!,p,out var pp,.5f,2f);
    Check(Navigation.FindPath(navigationMesh!,spawn,p,.5f,2f).Complete,role+" "+lane+" route unreachable: nav="+navigationMesh!.EntityId+" spawn="+ok0+ps+" lane="+ok1+pp+" unfiltered="+Navigation.FindPath(navigationMesh!,spawn,p).Complete);paths++;
    foreach(var site in new[]{"Bombsite A","Bombsite B"}) {
     Check(Navigation.FindPath(navigationMesh!,p,GameObject.Find(site)!.WorldPosition,.5f,2f).Complete,role+" "+lane+" -> "+site+" unreachable");paths++;
    }
   }
   for(int slot=0;slot<12;slot++) {
    var p=DefusalRules.SpawnPosition(spawn,slot,role=="Attack");
    Check(Navigation.ProjectPoint(navigationMesh!,p,out var projected,.5f,2f)&&Vector2.Distance(new(p.X,p.Z),new(projected.X,projected.Z))<1.1f,role+" spawn slot "+slot+" obstructed");
   }
  }
  foreach(var (from,to) in new[]{(new Vector3(-3,3,11),new Vector3(14,3,16)),(new Vector3(12,0,27),new Vector3(12,3,16)),(new Vector3(32,0,3),new Vector3(32,3,16)),(new Vector3(28,0,-23),new Vector3(17,3,-34))}) {
   Check(Navigation.FindPath(navigationMesh!,from,to,.5f,2f).Complete,"Terminal connection unreachable: "+from+" -> "+to);paths++;
  }
  foreach(var p in new[]{new Vector3(-25,3,8),new Vector3(-54,3,6),new Vector3(-24,3,32),new Vector3(-25,3,-26),new Vector3(17,3,-34),new Vector3(20,3,16),new Vector3(28,0,-23)})
   Check(Physics.Raycast(p+Vector3.UnitY*.3f,-Vector3.UnitY,1,out var hit)&&MathF.Abs(hit.Point.Y-p.Y)<.15f,"Incorrect walkable height at "+p);
  // The southwest stepped outline must not turn back into the old rectangular upper floor.
  foreach(var p in new[]{new Vector3(-55,4,22),new Vector3(-45,4,32)})
   Check(!Physics.Raycast(p,-Vector3.UnitY,5,out var outside)||outside.Point.Y<1,"Upper interior floor extends outside the supplied blueprint");
  // Existing rules must accept planting and defusing at both authored sites.
  for(int site=0;site<2;site++) {
   var pos=SitePosition(site);
   var rules=new DefusalRules(1,PlayerTeam.Alpha,1,pos,[SitePosition(0),SitePosition(1)],90);
   rules.Tick(DefusalRules.PlantSeconds,[new(1,PlayerTeam.Alpha,pos,true,true),new(2,PlayerTeam.Bravo,new(40,0,-20),true,false)]);
   Check(rules.Snapshot().Bomb==BombState.Planted&&rules.Snapshot().Site==site,"Plant failed at site "+site);
   rules.Tick(DefusalRules.DefuseSeconds,[new(1,PlayerTeam.Alpha,new(-54,3,6),true,false),new(2,PlayerTeam.Bravo,pos,true,true)]);
   Check(rules.Snapshot().Bomb==BombState.Defused,"Defuse failed at site "+site);
  }
  Check(DedicatedServerSettings.MapFor("Defusal","terminal")=="Terminal"&&DedicatedServerSettings.MapFor("Defusal","Foundry")=="Foundry","Defusal map catalog failed");
  Check(SitePosition(0).Y>3&&SitePosition(1).Y>3,"Bomb sites lost terminal elevation");
  Debug.Log("Terminal smoke: "+paths+" routes, 24 spawn slots and four surface heights.");
 }
}
public sealed class TerminalSmokeProbe : ScriptBehaviour
{
 [SerializedField] private string resultPath="";private int frames;
 public override void OnUpdate(float dt)
 {
  if(++frames!=20)return;
  try{GameObject.GetComponent<MultiplayerSession>()!.CheckTerminal();File.WriteAllText(resultPath,"PASS: native Terminal routes, both sites, three aircraft entrances, escalators, 24 spawn slots, traced interior boundary and collision surface heights, planting/defusing both sites and map catalog.");}
  catch(Exception ex){File.WriteAllText(resultPath,"FAIL: "+ex);}
 }
}
