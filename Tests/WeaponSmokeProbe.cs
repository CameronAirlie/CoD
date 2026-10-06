using System.Reflection;
using PlutoGE.ScriptCore;
using CoD.Scripts;

namespace CoD.Tests;

/// <summary>Test-only script compiled into an isolated assembly for the native runtime smoke scene.</summary>
public sealed class WeaponSmokeProbe : ScriptBehaviour
{
    [SerializedField] private string resultPath = "";
    [SerializedField] private GameObject? rifle = null;
    [SerializedField] private GameObject? pistol = null;
    [SerializedField] private GameObject? machineGun = null;
    [SerializedField] private GameObject? legacyRig = null;
    private PlayerController? _player;
    private int _frame;
    private int _reserve;
    private bool _failed;

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private void Invoke(string name) => typeof(PlayerController).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(_player, null);

    public override void OnUpdate(float deltaTime)
    {
        if (_failed) return;
        _player ??= GameObject.GetComponent<PlayerController>();
        try
        {
            switch (++_frame)
            {
                case 10:
                    Check(_player?.EquippedWeapon?.Id == "ar" && _player.Ammo == 30, "Native startup did not register all weapon rigs.");
                    Check(rifle?.Active == true && pistol?.Active == false && machineGun?.Active == false, "Multiple rigs are visible at startup.");
                    Check(legacyRig?.Active == false, "Legacy fallback rig remained visible behind the equipped weapon.");
                    _reserve = _player!.ReserveAmmo;
                    Invoke("TryFire");
                    Check(_player.Ammo == 30, "Initial draw did not block immediate fire.");
                    break;
                case 35:
                    Invoke("TryFire");
                    Check(_player!.Ammo == 29, "Rifle firing did not consume its own magazine.");
                    break;
                case 45:
                    Check(_player!.SelectWeaponSlot(1) && _player.Ammo == 12, "Pistol equip failed.");
                    Check(rifle?.Active == false && pistol?.Active == true, "Equip did not hide the old weapon.");
                    Invoke("TryFire");
                    Check(_player.Ammo == 12, "Draw animation did not block immediate fire.");
                    break;
                case 75:
                    Invoke("TryFire");
                    Check(_player!.Ammo == 11, "Pistol firing did not consume ammunition.");
                    Invoke("BeginReload");
                    Check(_player.IsReloading, "Pistol reload did not begin.");
                    break;
                case 85:
                    Check(_player!.SelectWeaponSlot(2) && !_player.IsReloading && _player.Ammo == 60, "Switching did not cancel reload.");
                    pistol!.GetComponent<WeaponHandBinding>()!.OnAnimationEvent(new AnimationEvent("ReloadFinish", "", 0, 0));
                    Check(_player.Ammo == 60 && _player.ReserveAmmo == _reserve, "Old rig event committed a cancelled reload.");
                    break;
                case 125:
                    Check(_player!.SelectWeaponSlot(1) && _player.Ammo == 11, "Switching refilled the pistol.");
                    break;
                case 175:
                    Invoke("BeginReload");
                    Check(_player!.IsReloading, "Reload did not start after drawing.");
                    break;
                case 275:
                    Check(!_player!.IsReloading && _player.Ammo == 12 && _player.ReserveAmmo == _reserve - 1,
                        "Native animation/timer reload did not transfer exactly one reserve round.");
                    break;
                case 285:
                    Check(_player!.SelectWeaponSlot(0) && _player.Ammo == 29, "Unequipped rifle magazine was lost.");
                    break;
                case 325:
                    _player!.EnterDeathState();
                    Check(rifle?.Active == false && !_player.SelectWeaponSlot(1), "Death did not hide hands/block switching.");
                    _player.ExitDeathState();
                    Check(rifle?.Active == true, "Respawn did not restore equipped hands.");
                    _player.ResetWeaponForRound();
                    Check(_player.Ammo == 30 && _player.SelectWeaponSlot(2) && _player.Ammo == 60, "Round reset did not refill all magazines.");
                    break;
                case 365:
                    File.WriteAllText(resultPath, "PASS: native managed startup, rig visibility, equip draw lock, firing, per-weapon ammo, reload cancellation, stale event isolation, reserve transfer, death/respawn and round reset.");
                    Input.CursorLocked = false;
                    break;
            }
        }
        catch (Exception error)
        {
            _failed = true;
            Input.CursorLocked = false;
            File.WriteAllText(resultPath, "FAIL at frame " + _frame + ": " + error);
            Debug.LogError(error.ToString());
        }
    }
}
