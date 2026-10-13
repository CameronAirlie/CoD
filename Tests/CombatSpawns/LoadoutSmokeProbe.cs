using PlutoGE.ScriptCore;
namespace CoD.Scripts;
public sealed partial class MultiplayerSession
{
 internal void CheckHardpointLoadouts()
 {
  static void Check(bool c,string m){if(!c)throw new Exception(m);}
  gameMode="Hardpoint";mode="Host";
  _playerStates.Clear();_remotePlayers.Clear();_humans.Clear();_bots.Clear();
  _playerStates[0]=new(PlayerTeam.Alpha,false);var human=new HumanSimulation();_humans[0]=human;
  SpawnHuman(0);
  Check(human.Life==1&&human.ActiveLoadout==CombatLoadout.Assault,"Initial preset spawn failed");
  Check(Account(0).WeaponMask==3&&human.Equipment.Weapons.SelectedSlot==0,"Assault weapons missing");
  human.Equipment.Weapons.Magazine=5;_playerStates[0].Health=35;
  var grenadeBefore=_playerStates[0].Grenades.Snapshot();
  AcceptLoadout(0,CombatLoadout.Defender);
  Check(human.Loadout==CombatLoadout.Defender&&human.ActiveLoadout==CombatLoadout.Assault,"Pending selection committed immediately");
  Check(human.Equipment.Weapons.Magazine==5&&Account(0).WeaponMask==3&&_playerStates[0].Health==35,"Selection changed live combat resources");
  Check(grenadeBefore.SequenceEqual(_playerStates[0].Grenades.Snapshot()),"Selection refilled grenades");
  _matchPhase=MatchPhase.Playing;_buyEndsAt=_time+20;
  var money=Account(0).Money;AcceptPurchase(0,"smoke");
  Check(Account(0).Money==money&&_playerStates[0].Grenades.Count("smoke")==0,"Hardpoint purchase accepted");
  SpawnHuman(0);
  Check(human.ActiveLoadout==CombatLoadout.Defender&&Account(0).WeaponMask==6,"Pending preset not committed on spawn");
  Check(human.Equipment.Weapons.SelectedSlot==2&&human.Equipment.Reserve==90&&human.Equipment.Kits==1&&human.Equipment.Plates==3,"Defender equipment incorrect");
  Check(_playerStates[0].Grenades.Count("molotov")==1&&_playerStates[0].Grenades.Count("flash")==0,"Defender throwables incorrect");
  _playerStates[0].Health=0;AcceptLoadout(0,CombatLoadout.Medic);
  Check(human.Loadout==CombatLoadout.Medic&&human.ActiveLoadout==CombatLoadout.Defender,"Dead player cannot queue selection");
  AcceptLoadout(0,(CombatLoadout)999);Check(human.Loadout==CombatLoadout.Medic,"Invalid loadout accepted");
  SpawnHuman(0);Check(human.ActiveLoadout==CombatLoadout.Medic&&Account(0).WeaponMask==2&&human.Equipment.Kits==3,"Medic spawn failed");
  AcceptLoadout(0,CombatLoadout.Assault);AcceptLoadout(0,CombatLoadout.Defender);SpawnHuman(0);
  Check(human.ActiveLoadout==CombatLoadout.Defender,"Latest choice did not win");
  _playerStates[0].Team=PlayerTeam.Spectator;AcceptLoadout(0,CombatLoadout.Assault);
  Check(human.Loadout==CombatLoadout.Defender,"Spectator queued combat loadout");_playerStates[0].Team=PlayerTeam.Alpha;
  var bot=new BotController(GameObject,GameObject.WorldPosition,1);_playerStates[-1001]=new(PlayerTeam.Bravo,true);
  EquipHardpointBot(-1001,bot);var preset=HardpointLoadouts.Find(HardpointLoadouts.ForBot(-1001));
  Check(Account(-1001).WeaponMask==preset.WeaponMask&&bot.Loadout.Equipment.Weapons.SelectedSlot==WeaponCatalog.DefaultSlot(preset.Id),"Bot preset did not spawn free");
  gameMode="Defusal";var old=human.Loadout;AcceptLoadout(0,CombatLoadout.Assault);Check(human.Loadout==old,"Defusal live selection behavior changed");
  gameMode="Hardpoint"; mode="Offline";
  SelectedLoadout=CombatLoadout.Assault; ApplyOfflineSpawnLoadout();
  SelectLoadout(CombatLoadout.Defender);
  Check(ActiveLoadout==CombatLoadout.Assault&&_offlineAccount.WeaponMask==3,"Offline selection applied immediately");
  OnLocalRespawn();
  Check(ActiveLoadout==CombatLoadout.Defender&&_offlineAccount.WeaponMask==6&&_offlineGrenades.Count("molotov")==1,"Offline spawn did not commit preset");
  Check(_inventory!.ReserveAmmo==90&&_inventory.ArmourPlates==3,"Offline supplies incorrect");
  GameObject.Find("RmlGameUI")!.GetComponent<UIManager>()!.SetInventoryOpen(true);
  SelectLoadout(CombatLoadout.Defender);
 }
}
public sealed class HardpointLoadoutSmokeProbe : ScriptBehaviour
{
 [SerializedField] private string resultPath="";private int frames;
 public override void OnUpdate(float dt)
 {
  if(++frames==15)
   try{GameObject.GetComponent<MultiplayerSession>()!.CheckHardpointLoadouts();}
   catch(Exception ex){File.WriteAllText(resultPath,"FAIL: "+ex);}
  if(frames==20&&!File.Exists(resultPath))
  {
   try{
    var widget=GameObject.Find("Inventory")!.GetComponent<RmlWidgetComponent>()!;
    var doc=widget.Document;
    if(doc.Element("loadout-state").Markup.Contains("NEXT SPAWN: DEFENDER")!=true)throw new Exception("Loadout menu status missing");
    if(!doc.Element("loadout-action-Defender").Markup.Contains("SELECTED"))throw new Exception("Selected card status missing");
    File.WriteAllText(resultPath,"PASS: native pending selection, dead selection, spawn commit, free fixed weapons/supplies/throwables, purchase rejection, invalid/spectator rejection, bot presets, offline spawn commit, Defusal isolation and runtime RML menu status.");
   }catch(Exception ex){File.WriteAllText(resultPath,"FAIL: "+ex);}
  }
 }
}
