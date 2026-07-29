using System;
using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>
/// A lightweight infantry bot: acquires the player, chases, strafes, checks
/// line-of-sight, fires in bursts, takes damage, and dies.
/// </summary>
public sealed class EnemySoldierBot : ScriptBehaviour
{
    [SerializedField] private GameObject? target;
    [SerializedField] private GameObject? animationObject = null;
    [SerializedField] private GameObject? gunAudioObject = null;
    [SerializedField] private GameObject? navigationTarget = null;
    [SerializedField] private string targetTag = "Player";
    [SerializedField] private string targetName = "Player";
    [SerializedField] private string targetDamageMethod = "TakeDamage";

    [SerializedField] private float health = 100.0f;
    [SerializedField] private float detectionRange = 45.0f;
    [SerializedField] private float fieldOfView = 120.0f;
    [SerializedField] private float attackRange = 28.0f;
    [SerializedField] private float firingAngle = 8.0f;
    [SerializedField] private float preferredRange = 14.0f;
    [SerializedField] private float moveSpeed = 4.2f;
    [SerializedField] private float strafeSpeed = 2.2f;
    [SerializedField] private float turnSharpness = 10.0f;
    [SerializedField] private float eyeHeight = 1.45f;
    [SerializedField] private float targetHeight = 0.75f;
    [SerializedField] private float tacticalRepathMin = 0.65f;
    [SerializedField] private float tacticalRepathMax = 1.25f;
    [SerializedField] private float flankAngle = 55.0f;
    [SerializedField] private float retreatRange = 7.0f;
    [SerializedField] private float reactionTime = 0.3f;
    [SerializedField] private float roundsPerMinute = 600.0f;
    [SerializedField] private int burstMin = 3;
    [SerializedField] private int burstMax = 7;
    [SerializedField] private float burstPauseMin = 0.35f;
    [SerializedField] private float burstPauseMax = 0.8f;
    [SerializedField] private float damagePerShot = 12.0f;
    [SerializedField] private float accuracyDegrees = 2.0f;

    [SerializedField] private string movementSpeedParameter = "MovementSpeed";
    [SerializedField] private string hasTargetParameter = "HasTarget";
    [SerializedField] private string shootTriggerParameter = "Shoot";
    [SerializedField] private string deadParameter = "Dead";
    [SerializedField] private string deathTriggerParameter = "Death";
    [SerializedField] private float deathCleanupDelay = 2.5f;

    private AnimationComponent? _animation;
    private SoundEmitterComponent? _gunAudio;
    private float _currentHealth;
    private float _time;
    private float _spottedAt = float.MaxValue;
    private float _nextShotAt;
    private float _nextBurstAt;
    private float _nextStrafeChangeAt;
    private float _destroyAt;
    private int _shotsLeft;
    private int _strafeDirection = 1;
    private uint _randomState;
    private bool _dead;
    private Vector3 _previousPosition;
    private Vector3 _navigationDestination;
    private Vector3 _lastKnownTargetPosition;
    private float _nextTacticalDecisionAt;

    public override void OnCreate()
    {
        _currentHealth = MathF.Max(1.0f, health);
        _randomState = EntityId * 747796405u + 2891336453u;
        ResolveTarget();

        var animationOwner = animationObject ?? GameObject;
        var audioOwner = gunAudioObject ?? GameObject;
        _animation = animationOwner.GetComponent<AnimationComponent>();
        _gunAudio = audioOwner.GetComponent<SoundEmitterComponent>();
        _previousPosition = GameObject.WorldPosition;
        _navigationDestination = _previousPosition;
        if (target is not null)
        {
            _lastKnownTargetPosition = target.WorldPosition;
            ChooseTacticalDestination(false);
        }
        UpdateNavigationTarget();
        SetAnimationFloat(movementSpeedParameter, 0.0f);
        SetAnimationBool(hasTargetParameter, false);
        SetAnimationBool(deadParameter, false);
    }

    public override void OnUpdate(float deltaTime)
    {
        _time += MathF.Max(0.0f, deltaTime);
        var position = GameObject.WorldPosition;
        var displacement = position - _previousPosition;
        displacement.Y = 0.0f;
        _previousPosition = position;
        var navigationSpeed = deltaTime > 0.0001f ? displacement.Length() / deltaTime : 0.0f;
        UpdateNavigationTarget();

        if (_dead)
        {
            if (_time >= _destroyAt)
                GameObject.Destroy();
            return;
        }

        if (target is null || !target.IsValid)
            ResolveTarget();
        if (target is null)
        {
            SetAnimationFloat(movementSpeedParameter, 0.0f);
            SetAnimationBool(hasTargetParameter, false);
            return;
        }

        var toTarget = target.WorldPosition - GameObject.WorldPosition;
        var horizontal = new Vector3(toTarget.X, 0.0f, toTarget.Z);
        var distance = horizontal.Length();
        if (distance > detectionRange || distance < 0.001f)
        {
            TurnTowardsNavigation(deltaTime);
            _spottedAt = float.MaxValue;
            SetAnimationFloat(movementSpeedParameter, 0.0f);
            SetAnimationBool(hasTargetParameter, false);
            return;
        }

        var direction = horizontal / distance;
        var hasLineOfSight = HasLineOfSight(distance);
        if (hasLineOfSight)
            _lastKnownTargetPosition = target.WorldPosition;

        if (_time >= _nextTacticalDecisionAt)
            ChooseTacticalDestination(hasLineOfSight);

        // Start turning as soon as an unobstructed target is in detection range.
        // Requiring the target to already be inside the vision cone here creates
        // a deadlock: a target outside the cone can never cause the bot to turn.
        if (hasLineOfSight)
            TurnTowards(direction, deltaTime);
        else
            TurnTowardsNavigation(deltaTime);

        var canSeeTarget = hasLineOfSight && IsInsideVisionCone(direction);
        SetAnimationBool(hasTargetParameter, canSeeTarget);
        if (canSeeTarget && _spottedAt == float.MaxValue)
            _spottedAt = _time;

        // Horizontal movement, collision-safe gravity, path following, and local
        // avoidance are owned by the native NavAgentComponent. Derive animation
        // speed from its actual motion instead of moving the entity a second time.
        SetAnimationFloat(movementSpeedParameter, navigationSpeed);

        if (canSeeTarget && IsFacingTarget(direction, firingAngle) &&
            distance <= attackRange && _time >= _spottedAt + reactionTime)
            UpdateWeapon();
        else
            _shotsLeft = 0;
    }

    public void TakeDamage(float amount)
    {
        if (_dead || amount <= 0.0f)
            return;

        _currentHealth -= amount;
        _spottedAt = MathF.Min(_spottedAt, _time);
        if (_currentHealth <= 0.0f)
            Die();
    }

    // Also accepts callers which do not provide a damage value.
    public void TakeDamage() => TakeDamage(25.0f);

    private void ResolveTarget()
    {
        target = !string.IsNullOrWhiteSpace(targetTag)
            ? GameObject.FindWithTag(targetTag)
            : null;
        target ??= !string.IsNullOrWhiteSpace(targetName)
            ? GameObject.Find(targetName)
            : null;
    }

    private Vector3 ChooseMovement(Vector3 forward, float distance, bool canSeeTarget)
    {
        if (!canSeeTarget)
            return Vector3.Zero;

        if (distance > preferredRange * 1.15f)
            return forward * moveSpeed;

        if (_time >= _nextStrafeChangeAt)
        {
            _strafeDirection = NextRandom01() < 0.5f ? -1 : 1;
            _nextStrafeChangeAt = _time + Lerp(0.7f, 1.8f, NextRandom01());
        }

        var right = new Vector3(-forward.Z, 0.0f, forward.X);
        var radial = distance < preferredRange * 0.7f ? -forward * moveSpeed * 0.65f : Vector3.Zero;
        return radial + right * (_strafeDirection * strafeSpeed);
    }

    private void ChooseTacticalDestination(bool hasLineOfSight)
    {
        if (target is null || navigationTarget is null)
            return;

        var targetPosition = hasLineOfSight ? target.WorldPosition : _lastKnownTargetPosition;
        var fromTarget = GameObject.WorldPosition - targetPosition;
        fromTarget.Y = 0.0f;
        var distance = fromTarget.Length();
        var radial = distance > 0.001f ? fromTarget / distance : GameObject.Forward;
        radial.Y = 0.0f;
        if (radial.LengthSquared() < 0.001f)
            radial = Vector3.UnitZ;
        radial = Vector3.Normalize(radial);

        var side = NextRandom01() < 0.5f ? -1.0f : 1.0f;
        Vector3 destination;
        if (!hasLineOfSight)
        {
            // Push toward the last contact from alternating angles instead of
            // tracking the player's live transform through walls.
            destination = targetPosition + RotateY(radial, side * flankAngle) * (preferredRange * 0.55f);
        }
        else if (distance < retreatRange)
        {
            // Break away from point-blank fights while biasing sideways so bots
            // do not simply reverse down the same path.
            destination = targetPosition + RotateY(radial, side * 25.0f) * preferredRange;
        }
        else if (distance > preferredRange * 1.35f)
        {
            // Close to weapon range on a shallow flank.
            destination = targetPosition + RotateY(radial, side * 20.0f) * preferredRange;
        }
        else
        {
            // Reposition around the player between bursts, producing the
            // lateral pressure expected from an arena/FPS bot.
            destination = targetPosition + RotateY(radial, side * flankAngle) * preferredRange;
        }

        destination.Y = targetPosition.Y;
        _navigationDestination = destination;
        UpdateNavigationTarget();
        _nextTacticalDecisionAt = _time +
            Lerp(tacticalRepathMin, MathF.Max(tacticalRepathMin, tacticalRepathMax), NextRandom01());
    }

    private void UpdateNavigationTarget()
    {
        if (navigationTarget is not null && navigationTarget.IsValid)
            navigationTarget.WorldPosition = _navigationDestination;
    }

    private bool HasLineOfSight(float distance)
    {
        if (target is null)
            return false;

        var origin = GameObject.WorldPosition + Vector3.UnitY * eyeHeight;
        var aimPoint = target.WorldPosition + Vector3.UnitY * targetHeight;
        var ray = aimPoint - origin;
        return Physics.Raycast(origin, ray, MathF.Max(distance + 2.0f, ray.Length() + 0.2f), GameObject, out var hit)
            && (hit.Entity.EntityId == target.EntityId || hit.Entity.HasTag(targetTag));
    }

    private bool IsInsideVisionCone(Vector3 direction)
        => IsFacingTarget(direction, Math.Clamp(fieldOfView, 0.0f, 360.0f) * 0.5f);

    private bool IsFacingTarget(Vector3 direction, float maximumAngle)
    {
        var forward = GameObject.Forward;
        forward.Y = 0.0f;
        if (forward.LengthSquared() < 0.001f)
            return false;

        forward = Vector3.Normalize(forward);
        var minimumDot = MathF.Cos(Math.Clamp(maximumAngle, 0.0f, 180.0f) * MathF.PI / 180.0f);
        return Vector3.Dot(forward, direction) >= minimumDot;
    }

    private void UpdateWeapon()
    {
        if (_shotsLeft <= 0)
        {
            if (_time < _nextBurstAt)
                return;
            _shotsLeft = NextInt(Math.Max(1, burstMin), Math.Max(burstMin, burstMax));
        }

        if (_time < _nextShotAt)
            return;

        FireShot();
        _shotsLeft--;
        _nextShotAt = _time + 60.0f / MathF.Max(1.0f, roundsPerMinute);
        if (_shotsLeft <= 0)
            _nextBurstAt = _time + Lerp(burstPauseMin, MathF.Max(burstPauseMin, burstPauseMax), NextRandom01());
    }

    private void FireShot()
    {
        if (target is null)
            return;

        var origin = GameObject.WorldPosition + Vector3.UnitY * eyeHeight;
        var aimPoint = target.WorldPosition + Vector3.UnitY * targetHeight;
        var direction = Vector3.Normalize(aimPoint - origin);
        direction = ApplySpread(direction, accuracyDegrees);

        if (Physics.Raycast(origin, direction, attackRange, GameObject, out var hit) &&
            (hit.Entity.EntityId == target.EntityId || hit.Entity.HasTag(targetTag)))
        {
            hit.Entity.TryInvoke(targetDamageMethod, damagePerShot);
        }

        _gunAudio?.PlayOneShot();
        SetAnimationTrigger(shootTriggerParameter);
    }

    private void TurnTowards(Vector3 direction, float deltaTime)
    {
        // PlutoGE entities face local -Z, so positive world X is a negative yaw.
        var desiredYaw = MathF.Atan2(-direction.X, -direction.Z) * 180.0f / MathF.PI;
        var rotation = Rotation;
        var difference = WrapAngle(desiredYaw - rotation.Y);
        rotation.Y += difference * (1.0f - MathF.Exp(-turnSharpness * deltaTime));
        Rotation = rotation;
    }

    private void TurnTowardsNavigation(float deltaTime)
    {
        var direction = _navigationDestination - GameObject.WorldPosition;
        direction.Y = 0.0f;
        if (direction.LengthSquared() > 0.001f)
            TurnTowards(Vector3.Normalize(direction), deltaTime);
    }

    private Vector3 ApplySpread(Vector3 direction, float degrees)
    {
        var radians = MathF.Max(0.0f, degrees) * MathF.PI / 180.0f;
        var yaw = (NextRandom01() * 2.0f - 1.0f) * radians;
        var pitch = (NextRandom01() * 2.0f - 1.0f) * radians;
        var right = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
        if (right.LengthSquared() < 0.001f)
            right = Vector3.UnitX;
        return Vector3.Normalize(direction + right * MathF.Tan(yaw) + Vector3.UnitY * MathF.Tan(pitch));
    }

    private void Die()
    {
        _dead = true;
        _navigationDestination = GameObject.WorldPosition;
        UpdateNavigationTarget();
        _destroyAt = _time + MathF.Max(0.0f, deathCleanupDelay);
        SetAnimationFloat(movementSpeedParameter, 0.0f);
        SetAnimationBool(hasTargetParameter, false);
        SetAnimationBool(deadParameter, true);
        SetAnimationTrigger(deathTriggerParameter);
    }

    private void SetAnimationFloat(string parameter, float value)
    {
        if (_animation is not null && !string.IsNullOrWhiteSpace(parameter))
            _animation.SetFloat(parameter, value);
    }

    private void SetAnimationBool(string parameter, bool value)
    {
        if (_animation is not null && !string.IsNullOrWhiteSpace(parameter))
            _animation.SetBool(parameter, value);
    }

    private void SetAnimationTrigger(string parameter)
    {
        if (_animation is not null && !string.IsNullOrWhiteSpace(parameter))
            _animation.SetTrigger(parameter);
    }

    private int NextInt(int minimum, int maximumInclusive)
        => minimum + (int)(NextRandom01() * (maximumInclusive - minimum + 1));

    private float NextRandom01()
    {
        _randomState = _randomState * 1664525u + 1013904223u;
        return (_randomState & 0x00ffffffu) / 16777216.0f;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0.0f, 1.0f);
    private static float WrapAngle(float angle) => (angle + 540.0f) % 360.0f - 180.0f;
    private static Vector3 RotateY(Vector3 direction, float degrees)
    {
        var radians = degrees * MathF.PI / 180.0f;
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return Vector3.Normalize(new Vector3(
            direction.X * cosine + direction.Z * sine,
            0.0f,
            -direction.X * sine + direction.Z * cosine));
    }
}
