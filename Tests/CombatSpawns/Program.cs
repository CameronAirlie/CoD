using CoD.Scripts;
using System.Numerics;
static void Check(bool value,string message) { if(!value) throw new Exception(message); }
var trigger=new WeaponTriggerBuffer();
Check(trigger.Consume(true,false,0,0,true),"Initial semi shot");
Check(!trigger.Consume(true,false,.3f,.2f,true),"Hold repeated semi shot");
trigger.Reset();trigger.Consume(true,false,0,0,true);trigger.Consume(false,false,.05f,.2f,true);
Check(!trigger.Consume(true,false,.1f,.2f,true),"Queued shot fired before cadence");
Check(trigger.Consume(false,false,.2f,.2f,true),"Released early click was dropped");
Check(!trigger.Consume(false,false,.3f,.2f,true),"Queued shot replayed");
trigger.Reset();trigger.Consume(true,false,0,.1f,true);trigger.Consume(false,false,.01f,.1f,false);
Check(!trigger.Consume(false,false,.1f,.1f,true),"Blocked action retained shot");
trigger.Reset();trigger.Consume(true,false,0,.1f,true);trigger.Cancel();
Check(!trigger.Consume(false,false,.1f,.1f,true),"Cancel retained shot");
trigger.Reset();Check(!trigger.Consume(true,false,0,.5f,true),"Far early shot fired");
Check(!trigger.Consume(true,false,.5f,.5f,true),"Stale click fired after draw");
trigger.Reset();trigger.Consume(true,false,0,.1f,true);
Check(!trigger.Consume(false,false,.5f,.1f,true),"Expired queue fired");
trigger.Reset();Check(trigger.Consume(true,true,0,0,true),"Auto initial");
Check(!trigger.Consume(true,true,.05f,.1f,true),"Auto bypassed cadence");
Check(trigger.Consume(true,true,.1f,.1f,true),"Auto hold lost");
var eq=new ServerEquipment();eq.Reset(CombatLoadout.Assault);
eq.Tick(PlayerCommand.Idle with {Slot=1},0,true);eq.TryFire(false,1,true);
Check(eq.TryFire(true,1,true),"Pistol first shot");var interval=eq.Weapons.Equipped.ShotInterval;
eq.TryFire(false,1.01f,true);Check(!eq.TryFire(true,1+interval-.05f,true),"Pistol early fire bypassed interval");
Check(eq.TryFire(false,1+interval+.001f,true),"Server did not release buffered pistol shot");
var ammo=eq.Weapons.Magazine;
Check(!eq.TryFire(false,2,true)&&eq.Weapons.Magazine==ammo,"Phantom queued shot consumed ammo");
eq.TryFire(true,2,true);eq.TryFire(false,2.01f,true);eq.TryFire(true,2+interval-.05f,true);
eq.Tick(PlayerCommand.Idle with {Slot=1,Reload=true},2+interval,true);
Check(!eq.TryFire(false,3,true),"Reload did not cancel queue");
var input=new ServerInput();input.Accept(PlayerCommand.Idle with{Sequence=1,Fire=true},0);
Check(input.Consume(.01f).Fire&&input.Consume(.02f).Fire,"Held input became false between ticks");
input.Accept(PlayerCommand.Idle with{Sequence=2,Fire=false},.03f);Check(!input.Consume(.03f).Fire,"Release ignored");
Console.WriteLine("PASS: semi-auto buffering, cadence, release, hold, cancellation, expiry, ammo and automatic fire.");
foreach(var team in new[]{PlayerTeam.Alpha,PlayerTeam.Bravo}) {
 var enemy=team==PlayerTeam.Alpha?PlayerTeam.Bravo:PlayerTeam.Alpha;
 Check(!BotSpawnResolver.HasClearance(Vector3.Zero,team,new(9,0,0),enemy,100),"Nearby enemy accepted");
 Check(!BotSpawnResolver.HasClearance(Vector3.Zero,team,new(0,50,0),enemy,100),"Vertical separation bypassed spawn safety");
 Check(BotSpawnResolver.HasClearance(Vector3.Zero,team,new(10,0,0),enemy,100),"Safe boundary rejected");
 Check(!BotSpawnResolver.HasClearance(Vector3.Zero,team,new(2,0,0),team,100),"Occupied ally spawn accepted");
 Check(BotSpawnResolver.HasClearance(Vector3.Zero,team,new(4,0,0),team,100),"Team separation treated as enemy");
 Check(BotSpawnResolver.HasClearance(Vector3.Zero,team,Vector3.Zero,enemy,0),"Dead enemy blocked spawn");
 Check(BotSpawnResolver.HasClearance(Vector3.Zero,team,Vector3.Zero,PlayerTeam.Spectator,100),"Spectator blocked spawn");
 Check(!BotSpawnResolver.TryResolve(Vector3.Zero,p=>p,p=>p,p=>BotSpawnResolver.HasClearance(p,team,new(3,0,0),enemy,100),out _),"Resolver fallback bypassed clearance");
}
Console.WriteLine("PASS: symmetric enemy/ally clearance, dead/spectator exclusion and placement fallback safety.");

foreach(var preset in HardpointLoadouts.Catalog) {
 var equipment=new ServerEquipment();equipment.Reset(preset.Id);
 var account=new EconomyAccount();account.SetWeaponLoadout(preset.WeaponMask);
 Check(account.Owns(1)&&account.Owns(equipment.Weapons.SelectedSlot),"Preset starts with an unowned weapon");
 Check(InventoryRules.Slots(equipment.Reserve,equipment.Kits,equipment.Plates)<=InventoryRules.Capacity,"Preset exceeds supply capacity");
 var supply=new GrenadeSupply();supply.SetLoadout(preset.Throwables);
 Check(supply.Remaining==preset.Throwables.Length&&supply.Remaining<=4,"Preset throwables invalid");
 Check(HardpointLoadouts.ForBot(int.MinValue) is CombatLoadout.Assault or CombatLoadout.Medic or CombatLoadout.Defender,"Bot choice invalid");
}
Console.WriteLine("PASS: all Hardpoint preset weapons, supplies, throwable limits and bot choices.");
