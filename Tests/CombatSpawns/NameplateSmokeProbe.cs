using System.Numerics;
using System.Reflection;
using PlutoGE.ScriptCore;
namespace CoD.Scripts;
public sealed class NameplateSmokeProbe : ScriptBehaviour
{
 [SerializedField] private string resultPath="";private int frames; private GameObject? actor;
 public override void OnUpdate(float dt)
 {
  if(++frames==10) {
   var eye=GameObject.GetComponent<PlayerController>()!.ViewPosition;
   actor=Prefab.Instantiate("project://Prefabs/Enemy.plutoprefab",eye+Vector3.UnitY*80,Vector3.Zero);
  }
  if(frames!=15)return;
  try {
   static void Check(bool c,string m){if(!c)throw new Exception(m);}
   var origin=GameObject.GetComponent<PlayerController>()!.ViewPosition;
   Check(actor is not null && actor.IsValid,"Probe actor did not instantiate");
   var bot=actor!.GetComponent<EnemySoldierBot>()!;bot.SetRemoteProxyMode();
   actor.WorldPosition=origin+Vector3.UnitY*80;
   const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
   var tag=(GameObject)typeof(EnemySoldierBot).GetField("networkNameplate",flags)!.GetValue(bot)!;
   void Refresh(){typeof(EnemySoldierBot).GetField("_nextNameplateVisibilityAt",flags)!.SetValue(bot,0f);typeof(EnemySoldierBot).GetMethod("UpdateNameplateVisibility",flags)!.Invoke(bot,null);}
   bot.ConfigureNetworkNameplate("FRIEND",true,(int)PlayerTeam.Alpha);Refresh();
   Check(tag.Active,"Unoccluded friendly tag at 80 metres was hidden");
   bot.ConfigureNetworkNameplate("ENEMY",false,(int)PlayerTeam.Bravo);
   Check(!tag.Active,"Enemy tag did not hide immediately on team change");Refresh();
   Check(!tag.Active,"Visible distant enemy showed a tag");
   actor.WorldPosition=origin+Vector3.UnitY*2;Refresh();Check(!tag.Active,"Nearby enemy showed a tag");
   actor.WorldPosition=origin+Vector3.UnitY*80;bot.ConfigureNetworkNameplate("FRIEND",true,(int)PlayerTeam.Alpha);Refresh();
   Check(tag.Active,"Switch back to friendly did not restore visible tag");
   actor.WorldPosition=origin+Vector3.UnitX*80;Refresh();Check(!tag.Active,"Friendly tag appeared through yard geometry");
   actor.WorldPosition=origin+Vector3.UnitY*80;Refresh();Check(tag.Active,"Friendly tag failed to reappear after occlusion");
   bot.SetSpectatorHidden(true);Refresh();Check(!tag.Active,"Observed actor showed its own tag");
   bot.SetSpectatorHidden(false);Refresh();Check(tag.Active,"Friendly tag failed after spectator visibility restore");
   File.WriteAllText(resultPath,"PASS: native long-distance friendly visibility, occlusion, nearby/distant enemy suppression, immediate team changes and spectator hiding.");
  } catch(Exception ex){File.WriteAllText(resultPath,"FAIL: "+ex);}
  finally{actor?.Destroy();}
 }
}
