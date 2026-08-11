using System;
using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>
/// An infantry bot which repeatedly takes a useful firing position, holds it,
/// and engages the player whenever it has line-of-sight.
/// </summary>
public sealed class EnemySoldierBot : ScriptBehaviour
{
    public bool IsExternalNavigationControlled => _externalNavigationControl;
    [SerializedField] private bool remoteProxy = false;
    [SerializedField] private GameObject? target;
    [SerializedField] private GameObject? animationObject = null;
    [SerializedField] private GameObject? gunAudioObject = null;
    [SerializedField] private GameObject? navigationTarget = null;
    [SerializedField] private GameObject? navigationMesh = null;
    [SerializedField] private float navigationAgentRadius = 0.5f;
    [SerializedField] private float navigationAgentHeight = 2.0f;
    [SerializedField] private GameObject? networkNameplate = null;
    [SerializedField] private string targetTag = "Player";
    [SerializedField] private string targetName = "Player";
    [SerializedField] private string targetDamageMethod = "TakeDamage";

    [SerializedField] private float health = 100.0f;
    [SerializedField] private float detectionRange = 45.0f;
    [SerializedField] private float fieldOfView = 120.0f;
    [SerializedField] private float attackRange = 28.0f;
    [SerializedField] private float firingAngle = 8.0f;
    [SerializedField] private float preferredRange = 14.0f;
    [SerializedField] private float centrePositionWeight = 1.25f;
    [SerializedField] private float turnSharpness = 10.0f;
    [SerializedField] private float stationaryFireSpeed = 0.2f;
    [SerializedField] private float eyeHeight = 1.45f;
    [SerializedField] private float targetHeight = 0.75f;
    [SerializedField] private float positionSearchRadiusMin = 8.0f;
    [SerializedField] private float positionSearchRadiusMax = 22.0f;
    [SerializedField] private int positionSamples = 12;
    [SerializedField] private float arrivalDistance = 1.25f;
    [SerializedField] private float positionMoveTimeout = 8.0f;
    [SerializedField] private float holdTimeMin = 2.0f;
    [SerializedField] private float holdTimeMax = 4.0f;
    [SerializedField] private float reactionTime = 0.3f;
    [SerializedField] private float perceptionRefreshInterval = 0.1f;
    [SerializedField] private float aimLineOfSightGrace = 0.5f;
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
    [SerializedField] private string hitTriggerParameter = "Hit";
    [SerializedField] private string deadParameter = "Dead";
    [SerializedField] private string deathTriggerParameter = "Death";
    [SerializedField] private float deathCleanupDelay = 2.5f;

    private AnimationComponent? _animation;
    private SoundEmitterComponent? _gunAudio;
    private RigidbodyComponent? _body;
    private float _currentHealth;
    private float _time;
    private float _spottedAt = float.MaxValue;
    private float _nextShotAt;
    private float _nextBurstAt;
    private float _destroyAt;
    private int _shotsLeft;
    private uint _randomState;
    private bool _dead;
    private Vector3 _previousPosition;
    private Vector3 _latePreviousPosition;
    private Vector3 _navigationDestination;
    private Vector3 _lastKnownTargetPosition;
    private float _holdUntil;
    private float _positionMoveDeadline;
    private float _nextPerceptionRefreshAt;
    private float _lastLineOfSightAt = float.MinValue;
    private float _lastAnimationSpeed = float.NaN;
    private bool _lastHasTarget;
    private bool _hasLastHasTarget;
    private bool _lastDead;
    private bool _hasLastDead;
    private bool _cachedLineOfSight;
    private bool _isHolding;
    private bool _externalNavigationControl;
    private bool _remoteProxyMode;
    private bool _externalAiming;
    private RmlDocument? _networkNameplateDocument;
    private string _networkNameplateText = string.Empty;
    private bool _networkNameplateDirty;
    private float _turnDirection = 1.0f;

    public override void OnCreate()
    {
        _remoteProxyMode = remoteProxy;
        _externalNavigationControl = remoteProxy;
        _currentHealth = MathF.Max(1.0f, health);
        _randomState = EntityId * 747796405u + 2891336453u;
        navigationMesh ??= GameObject.Find("Navmesh");
        if (!_remoteProxyMode)
            ResolveTarget();

        var animationOwner = animationObject ?? GameObject;
        var audioOwner = gunAudioObject ?? GameObject;
        _animation = animationOwner.GetComponent<AnimationComponent>();
        _gunAudio = audioOwner.GetComponent<SoundEmitterComponent>();
        _body = GameObject.GetComponent<RigidbodyComponent>();
        _previousPosition = GameObject.WorldPosition;
        _latePreviousPosition = _previousPosition;
        _navigationDestination = _previousPosition;
        _nextPerceptionRefreshAt = NextRandom01() * MathF.Max(0.02f, perceptionRefreshInterval);
        if (!_remoteProxyMode && target is not null)
        {
            _lastKnownTargetPosition = target.WorldPosition;
            ChooseTacticalDestination();
        }
        UpdateNavigationTarget();
        SetAnimationFloat(movementSpeedParameter, 0.0f);
        SetAnimationBool(hasTargetParameter, false);
        SetAnimationBool(deadParameter, false);
    }

    public override void OnUpdate(float deltaTime)
    {
        RefreshNetworkNameplate();
        _time += MathF.Max(0.0f, deltaTime);
        var position = GameObject.WorldPosition;
        var displacement = position - _previousPosition;
        displacement.Y = 0.0f;
        _previousPosition = position;
        var velocity = _body is not null && !_body.IsKinematic
            ? _body.Velocity
            : (deltaTime > 0.0001f ? displacement / deltaTime : Vector3.Zero);
        velocity.Y = 0.0f;
        var navigationSpeed = velocity.Length();

        if (_dead)
        {
            if (_time >= _destroyAt)
                GameObject.Destroy();
            return;
        }


        // TDM bots are directed by MultiplayerSession, while the native
        // NavAgentComponent on this prefab remains responsible for pathfinding,
        // collision-safe movement, and local avoidance.
        if (_externalNavigationControl)
        {
            if (_remoteProxyMode)
                _navigationDestination = GameObject.WorldPosition;
            UpdateNavigationTarget();
            SetAnimationFloat(movementSpeedParameter, navigationSpeed);
            SetAnimationBool(hasTargetParameter, !_remoteProxyMode && _externalAiming);
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

        var targetPosition = target.WorldPosition;
        var toTarget = targetPosition - position;
        var horizontal = new Vector3(toTarget.X, 0.0f, toTarget.Z);
        var distanceSquared = horizontal.LengthSquared();
        var detectionRangeSquared = detectionRange * detectionRange;
        if (distanceSquared > detectionRangeSquared || distanceSquared < 0.000001f)
        {
            TurnTowardsNavigation(deltaTime);
            _spottedAt = float.MaxValue;
            SetAnimationFloat(movementSpeedParameter, 0.0f);
            SetAnimationBool(hasTargetParameter, false);
            return;
        }

        var distance = MathF.Sqrt(distanceSquared);
        var direction = horizontal / distance;
        if (_time >= _nextPerceptionRefreshAt)
        {
            _cachedLineOfSight = HasLineOfSight(position, targetPosition, distance);
            _nextPerceptionRefreshAt = _time + MathF.Max(0.02f, perceptionRefreshInterval);
        }
        var hasLineOfSight = _cachedLineOfSight;
        if (hasLineOfSight)
        {
            _lastKnownTargetPosition = targetPosition;
            _lastLineOfSightAt = _time;
        }

        UpdatePositioning(position);
        // The navigation marker is a child of the bot in the prefab. Pinning
        // its world position every frame prevents it travelling along with its
        // parent and creating a destination the bot can never reach.
        UpdateNavigationTarget();

        // Keep aiming briefly through single-frame visibility failures. Without
        // this hysteresis the bot snaps between the player and its waypoint as
        // raycasts skim corners or another enemy crosses the shot.
        var isStationary = navigationSpeed <= MathF.Max(0.01f, stationaryFireSpeed);
        var shouldAimAtTarget = hasLineOfSight ||
            _time <= _lastLineOfSightAt + MathF.Max(0.0f, aimLineOfSightGrace);
        // NavAgentComponent owns rotation while moving. Scripted aiming only
        // takes over after locomotion has stopped, avoiding transform writes
        // fighting the dynamic rigidbody every frame.
        if (isStationary)
        {
            if (shouldAimAtTarget)
                TurnTowards(direction, deltaTime);
            else
                TurnTowardsNavigation(deltaTime);
        }

        var canSeeTarget = hasLineOfSight && IsInsideVisionCone(direction);
        SetAnimationBool(hasTargetParameter, isStationary && canSeeTarget);
        if (canSeeTarget && _spottedAt == float.MaxValue)
            _spottedAt = _time;

        // Horizontal movement, collision-safe gravity, path following, and local
        // avoidance are owned by the native NavAgentComponent. Derive animation
        // speed from its actual motion instead of moving the entity a second time.
        SetAnimationFloat(movementSpeedParameter, navigationSpeed);

        if (isStationary && canSeeTarget && IsFacingTarget(direction, firingAngle) &&
            distance <= attackRange && _time >= _spottedAt + reactionTime)
            UpdateWeapon();
        else
            _shotsLeft = 0;
    }

    public override void OnLateUpdate(float deltaTime)
    {
        var position = GameObject.WorldPosition;
        var actualTravel = position - _latePreviousPosition;
        actualTravel.Y = 0.0f;
        _latePreviousPosition = position;

        if (_dead || _remoteProxyMode || deltaTime <= 0.0f)
            return;

        var travelDistance = actualTravel.Length();
        var actualSpeed = travelDistance / deltaTime;
        if (actualSpeed > MathF.Max(0.01f, stationaryFireSpeed) &&
            !_externalAiming && travelDistance > 0.0001f)
            TurnTowards(actualTravel / travelDistance, deltaTime);
    }

    public void TakeDamage(float amount)
    {
        if (_externalNavigationControl || _dead || amount <= 0.0f)
            return;

        _currentHealth -= amount;
        _spottedAt = MathF.Min(_spottedAt, _time);
        if (_currentHealth <= 0.0f)
            Die();
        else
            SetAnimationTrigger(hitTriggerParameter);
    }

    // Also accepts callers which do not provide a damage value.
    public void TakeDamage() => TakeDamage(25.0f);

    public void SetExternalNavigationDestination(float x, float y, float z)
    {
        _externalNavigationControl = true;
        _remoteProxyMode = false;
        _externalAiming = false;
        _dead = false;
        _navigationDestination = new Vector3(x, y, z);
        UpdateNavigationTarget();
    }

    public void ResetExternalNavigation(float x, float y, float z)
    {
        _externalNavigationControl = true;
        _remoteProxyMode = false;
        _externalAiming = false;
        _dead = false;
        _currentHealth = MathF.Max(1.0f, health);
        GameObject.WorldPosition = new Vector3(x, y, z);
        _previousPosition = GameObject.WorldPosition;
        _latePreviousPosition = _previousPosition;
        _navigationDestination = _previousPosition;
        UpdateNavigationTarget();
        SetAnimationBool(deadParameter, false);
    }

    public void SetRemoteProxyMode()
    {
        _externalNavigationControl = true;
        _remoteProxyMode = true;
        _externalAiming = false;
        _dead = false;
        _navigationDestination = GameObject.WorldPosition;
        UpdateNavigationTarget();
        SetAnimationBool(deadParameter, false);
    }

    public void PlayExternalShootAnimation()
    {
        if (!_externalNavigationControl) return;
        SetAnimationBool(hasTargetParameter, true);
        SetAnimationTrigger(shootTriggerParameter);
        _gunAudio?.PlayOneShot();
    }

    public void PlayExternalHitAnimation()
    {
        if (_externalNavigationControl)
            SetAnimationTrigger(hitTriggerParameter);
    }

    public void SetExternalAiming(bool aiming)
    {
        if (_externalNavigationControl)
        {
            _externalAiming = aiming;
            SetAnimationBool(hasTargetParameter, aiming);
        }
    }

    public void ConfigureNetworkNameplate(string username, bool friendly)
    {
        if (networkNameplate is null || !networkNameplate.IsValid) return;
        networkNameplate.Active = friendly;
        _networkNameplateText = friendly ? $"[FRIENDLY] {username}" : string.Empty;
        _networkNameplateDirty = friendly;
        if (!friendly) return;
        _networkNameplateDocument ??=
            new RmlDocument($"UI/friendly-nameplate.rml#entity:{networkNameplate.EntityId}");
        RefreshNetworkNameplate();
    }

    public override void OnDestroy()
    {
        _networkNameplateDocument?.Dispose();
        _networkNameplateDocument = null;
    }

    private void RefreshNetworkNameplate()
    {
        if (!_networkNameplateDirty || _networkNameplateDocument is null) return;
        var label = _networkNameplateDocument.Element("friendly-name");
        label.Markup = _networkNameplateText;
        _networkNameplateDirty = !label.SetClass("ready", true);
    }

    private void ResolveTarget()
    {
        target = !string.IsNullOrWhiteSpace(targetTag)
            ? GameObject.FindWithTag(targetTag)
            : null;
        target ??= !string.IsNullOrWhiteSpace(targetName)
            ? GameObject.Find(targetName)
            : null;
    }

    private void UpdatePositioning(Vector3 position)
    {
        var offset = _navigationDestination - position;
        offset.Y = 0.0f;
        if (!_isHolding && offset.LengthSquared() <= arrivalDistance * arrivalDistance)
        {
            _isHolding = true;
            _holdUntil = _time + Lerp(holdTimeMin, MathF.Max(holdTimeMin, holdTimeMax), NextRandom01());
            _navigationDestination = position;
            UpdateNavigationTarget();
        }
        else if (_isHolding && _time >= _holdUntil)
        {
            ChooseTacticalDestination();
        }
        else if (!_isHolding && _time >= _positionMoveDeadline)
        {
            // A sampled point may be outside the baked navigation mesh. Give
            // up after a bounded time and select another position.
            ChooseTacticalDestination();
        }
    }

    private void ChooseTacticalDestination()
    {
        if (target is null || navigationTarget is null)
            return;

        var playerPosition = target.WorldPosition;
        var botPosition = GameObject.WorldPosition;
        var bestPosition = _lastKnownTargetPosition;
        var bestScore = float.MinValue;
        var samples = Math.Clamp(positionSamples, 4, 32);
        var minimumRadius = MathF.Max(2.0f, positionSearchRadiusMin);
        var maximumRadius = MathF.Max(minimumRadius, positionSearchRadiusMax);

        // Test positions around the player. Clear shooting lanes score highest;
        // weapon-range positions are preferred, with a small travel penalty so
        // the bot does not cross the whole arena for a marginal improvement.
        var angleOffset = NextRandom01() * 360.0f;
        for (var index = 0; index < samples; index++)
        {
            var angle = angleOffset + index * (360.0f / samples);
            var radius = Lerp(minimumRadius, maximumRadius, NextRandom01());
            var candidate = playerPosition + RotateY(Vector3.UnitZ, angle) * radius;
            candidate.Y = botPosition.Y;
            if (!TryResolveNavigablePosition(candidate, out candidate))
                continue;

            var shotDistance = Vector3.Distance(candidate, playerPosition);
            var clearShot = HasLineOfSightFrom(candidate, playerPosition);
            var rangeError = MathF.Abs(shotDistance - preferredRange);
            var travelDistance = Vector3.Distance(botPosition, candidate);
            var centreDistance = navigationMesh is null
                ? 0.0f
                : HorizontalDistance(candidate, navigationMesh.WorldPosition);
            var score = (clearShot ? 1000.0f : 0.0f) - rangeError * 4.0f -
                travelDistance * 0.35f - centreDistance * MathF.Max(0.0f, centrePositionWeight);
            if (score > bestScore)
            {
                bestScore = score;
                bestPosition = candidate;
            }
        }

        // If no sampled firing lane is clear, advance toward the last place the
        // player was seen. The next search will fan out from there.
        if (bestScore < 0.0f &&
            !TryResolveNavigablePosition(_lastKnownTargetPosition, out bestPosition))
            return;

        _navigationDestination = bestPosition;
        _isHolding = false;
        _positionMoveDeadline = _time + MathF.Max(1.0f, positionMoveTimeout);
        UpdateNavigationTarget();
    }

    private bool TryResolveNavigablePosition(Vector3 desiredPosition, out Vector3 position)
    {
        position = GameObject.WorldPosition;
        if (navigationMesh is null || !navigationMesh.IsValid ||
            !Navigation.ProjectPoint(
                navigationMesh, desiredPosition, out var projected,
                navigationAgentRadius, navigationAgentHeight))
            return false;

        var path = Navigation.FindPath(
            navigationMesh, GameObject.WorldPosition, projected,
            navigationAgentRadius, navigationAgentHeight);
        if (!path.Complete || path.Points.Count == 0)
            return false;

        position = projected;
        return true;
    }

    private void UpdateNavigationTarget()
    {
        if (navigationTarget is not null && navigationTarget.IsValid)
            navigationTarget.WorldPosition = _navigationDestination;
    }

    private bool HasLineOfSight(Vector3 position, Vector3 targetPosition, float distance)
    {
        if (target is null)
            return false;

        var origin = position + Vector3.UnitY * eyeHeight;
        var aimPoint = targetPosition + Vector3.UnitY * targetHeight;
        var ray = aimPoint - origin;
        var rayLength = ray.Length();
        if (rayLength < 0.001f)
            return true;
        return Physics.Raycast(origin, ray / rayLength, MathF.Max(distance + 2.0f, rayLength + 0.2f), GameObject, out var hit)
            && (hit.Entity.EntityId == target.EntityId || hit.Entity.HasTag(targetTag));
    }

    private bool HasLineOfSightFrom(Vector3 position, Vector3 targetPosition)
    {
        var ray = targetPosition + Vector3.UnitY * targetHeight -
                  (position + Vector3.UnitY * eyeHeight);
        var rayLength = ray.Length();
        return target is not null && rayLength >= 0.001f &&
            Physics.Raycast(position + Vector3.UnitY * eyeHeight, ray / rayLength, rayLength + 0.2f, GameObject, out var hit) &&
            (hit.Entity.EntityId == target.EntityId || hit.Entity.HasTag(targetTag));
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
        if (MathF.Abs(MathF.Abs(difference) - 180.0f) < 0.1f)
            difference = 180.0f * _turnDirection;
        else if (MathF.Abs(difference) > 0.1f)
            _turnDirection = MathF.Sign(difference);
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
        if (_animation is null || string.IsNullOrWhiteSpace(parameter))
            return;
        if (!float.IsNaN(_lastAnimationSpeed) && MathF.Abs(_lastAnimationSpeed - value) < 0.01f)
            return;
        _lastAnimationSpeed = value;
        _animation.SetFloat(parameter, value);
    }

    private void SetAnimationBool(string parameter, bool value)
    {
        if (_animation is null || string.IsNullOrWhiteSpace(parameter))
            return;

        if (parameter == hasTargetParameter)
        {
            if (_hasLastHasTarget && _lastHasTarget == value)
                return;
            _hasLastHasTarget = true;
            _lastHasTarget = value;
        }
        else if (parameter == deadParameter)
        {
            if (_hasLastDead && _lastDead == value)
                return;
            _hasLastDead = true;
            _lastDead = value;
        }
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
    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        var offset = first - second;
        offset.Y = 0.0f;
        return offset.Length();
    }
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
