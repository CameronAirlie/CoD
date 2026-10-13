using PlutoGE.ScriptCore;
namespace CoD.Scripts;
public sealed partial class MultiplayerSession
{
 internal void CheckSpawnSafety()
 {
  static void Check(bool c,string m) {if(!c) throw new Exception(m);}
  _playerStates.Clear();_remotePlayers.Clear();_humans.Clear();_bots.Clear();
  _playerStates[0]=new(PlayerTeam.Alpha,false);
  var oldNavigation=navigationMesh;
  // Four occupied authored corners, with random navigation unavailable, must defer.
  navigationMesh=null;
  var names=new[]{"North West","North East","South East","South West"};
  for(int i=0;i<4;i++) {
   var marker=GameObject.Find("Spawn "+names[i])!;
   _playerStates[100+i]=new(PlayerTeam.Bravo,true);
   _remotePlayers[100+i]=new(marker,marker.WorldPosition,0);
   Check(!IsBotSpawnClear(0,marker.WorldPosition),"Hostile corner accepted");
  }
  Check(!TryBotSpawnPosition(0,out _),"Blocked corner selection bypassed safety");
  var human=new HumanSimulation();_humans[0]=human;SpawnHuman(0);
  Check(human.Life==0&&_playerStates[0].Health==0,"Human revived without a safe spawn");
  var corpse=GameObject.Find("Spawn North West")!;
  var previous=new BotController(corpse,corpse.WorldPosition,1);
  _playerStates[-1000]=new(PlayerTeam.Alpha,true);
  Check(ReferenceEquals(RespawnBot(-1000,previous),previous)&&_playerStates[-1000].Health==0,"Bot reused an unsafe previous spawn");
  // Make three corners safe and verify a human actually relocates away from its old spawn.
  for(int i=1;i<4;i++){_playerStates.Remove(100+i);_remotePlayers.Remove(100+i);}
  human.Spawn=corpse.WorldPosition;navigationMesh=oldNavigation;
  SpawnHuman(0);
  Check(human.Life==1&&IsBotSpawnClear(0,human.Spawn),"Human failed safe dynamic respawn");
  Check(HorizontalDistance(human.Spawn,corpse.WorldPosition)>=10,"Human reused hostile spawn");
  // Initial unregistered bots must use their assigned team, including Bravo vs Alpha.
  _playerStates.Clear();_remotePlayers.Clear();
  _playerStates[0]=new(PlayerTeam.Alpha,false);
  Check(!IsBotSpawnClear(-2000,GameObject.WorldPosition,PlayerTeam.Bravo),"New Bravo bot ignored Alpha player");
 }
}
public sealed class CombatSpawnSmokeProbe : ScriptBehaviour
{
 [SerializedField] private string resultPath="";
 private int frames;
 public override void OnUpdate(float dt)
 {
  if(++frames!=15)return;
  try{GameObject.GetComponent<MultiplayerSession>()!.CheckSpawnSafety();File.WriteAllText(resultPath,"PASS: native occupied-corner rejection, human/bot deferred respawn, dynamic human relocation, and initial opposite-team bot safety.");}
  catch(Exception ex){File.WriteAllText(resultPath,"FAIL: "+ex);}
 }
}
