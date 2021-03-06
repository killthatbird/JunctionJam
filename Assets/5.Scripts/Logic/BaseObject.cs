﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BaseObject : PooledObject
{
    //COMPONENTS
    protected NavMeshAgent navMeshAgent;
    protected ObjectRenderer objectRenderer;
    [SerializeField] //maybe read data from JSON later for easier configuration ?
    public ObjectData objectData = new ObjectData();
    //[SerializeField] //serialize field for easier debuggin, don't need now
    protected AnimatorWrapper animatorWrapper;
    protected Animator childAnimator;
    //MANAGERS
    //[SerializeField]
    protected ObjectManager objectManager; //Object's manager, for team specific action
    protected PlayerManager playerManager; //For player's team function, like get nearest hero
    protected EnemyManager enemyManager; //For enemy's team function, 
    protected ProjectileManager projectileManager;
    protected CameraController cameraController; //need for screen shake
    protected MouseController mouseController; //need for hero only, might change later though

    //public variables (for other unit to check
    public bool isEnemy;
    protected ObjectState objectState;
    
    //Private variables, mostly for flagging and checking 
    protected float idleCountDown; //The amount of frame to check for new target when idle
    protected bool isDamageDeal;
    protected float attackCountUp;

    protected bool isFollowingAlly;
    protected BaseObject followingAlly;
    //[SerializeField]
    protected long targetID;
    protected BaseObject targetObject;
    
    protected Vector3 targetPosition;
    protected bool isHavingTargetPosition;
    //The position of the target object is always changing. It would hurt performance 
    //if we update the target position every frame.
    protected int objectChargeCountdown;

    public virtual ObjectType GetObjectType() { return ObjectType.Invalid; }

    public virtual ProjectileType GetProjectileType() { return ProjectileType.Invalid; }

    public virtual CorpseType GetCorpseType() { return CorpseType.Invalid; }

    public ObjectState GetObjectState() { return objectState;}

    public virtual void Init(ObjectManager _objectManager, bool isEnemyTeam, int objectLevel)
    {
        //for testing and easier debuging.
        gameObject.name = GetObjectType().ToString() + "_" + id.ToString();
        isEnemy = isEnemyTeam;
        objectManager = _objectManager;
        OnFirstInit();
        PrepareComponent();
        
        UpdateStatsByLevel(objectLevel);
        SetState(ObjectState.Idle);
        isDamageDeal = false;
        isHavingTargetPosition = false;
        isFollowingAlly = false;
        objectChargeCountdown = GameConstant.objectChargeCountdownValue;
        idleCountDown = GameConstant.idleCheckFrequency;
        healthRegenCountUp = 0f;
    }

    protected override void OnFirstInit()
    {
        base.OnFirstInit();
        projectileManager = Directors.instance.projectileManager;
        cameraController = Directors.instance.cameraController;
        mouseController = Directors.instance.mouseController;
        playerManager = Directors.instance.playerManager;
        enemyManager = Directors.instance.enemyManager;
    }

    protected virtual void PrepareComponent()
    {
        if (navMeshAgent == null)
        {
            navMeshAgent = GetComponent<NavMeshAgent>();
        }
        navMeshAgent.speed = objectData.moveSpeed;
        childAnimator = GetComponentInChildren<Animator>();
        animatorWrapper = new AnimatorWrapper(childAnimator);

        if (objectRenderer == null)
        {
            objectRenderer = GetComponentInChildren<ObjectRenderer>();
            objectRenderer.InitRenderer(this);
        }
        objectRenderer.UpdateHealthBar(1f);
        objectRenderer.OnParentObjectRespawn();
    }

    //TODO: make this function read data externally
    //TODO: maybe make object stats not depend on level. It is very confusing for the player.
    public virtual void UpdateStatsByLevel(int level = 1)
    {
        objectData.level = level;
        objectData.sight = objectData.attackRange + 3f;
        objectData.maxHealth = objectData.baseMaxHealth+ (int)(objectData.baseMaxHealth * (level -1) * GameConstant.normalCreepDamageIncreasePerLevel);
        objectData.damange = objectData.baseDamage+ (int)(objectData.baseDamage * (level - 1) * GameConstant.normalCreepDamageIncreasePerLevel);
        objectData.health = objectData.maxHealth;
        objectData.currentSpecialCoolDown = objectData.specialCoolDown;
    }

    //Update is called every frame by this object manager.
    //DON'T OVERRIDE THIS UNLESS CARGO KART!!!.
    public virtual void DoUpdate()
    {
        switch (objectState)
        {
            case ObjectState.Idle:
                ObjectIdle();
                break;
            case ObjectState.Run:
                ObjectRunning();
                break;
            case ObjectState.Charge:
                ObjectCharging();
                break;
            case ObjectState.Attack:
                ObjectAttack();
                break;
            case ObjectState.Stun:
                ObjectStun();
                break;
            case ObjectState.Special:
                ObjectSpecial();
                break;
            case ObjectState.Die:
                WhileObjectDie();
                break;
        }
        AdditionalUpdateFunction();
    }

    //What should this object do when a state is changed ????
    protected void SetState(ObjectState state)
    {
        switch (state)
        {
            case ObjectState.Idle:
                //Hot fix, so that when the hero respawn, he will not move to his previous position.
                //Is this one a hot fix or a neccessary logic step ????
                navMeshAgent.Stop();
                navMeshAgent.SetDestination(transform.position);

                //Another hot fix, to fix the wrong rotation of object when finishing an animation
                //There is still a weird rotation sometime but I can not catch it all the time :/
                //Debug.Log("Local Rotation " + objectRenderer.gameObject.transform.localRotation.ToString());
                objectRenderer.gameObject.transform.localRotation = Quaternion.Euler(Vector3.zero);
                //Debug.Log("Local Rotation after set - " + objectRenderer.gameObject.transform.localRotation.ToString());

                animatorWrapper.AddTriggerToQueue("EnterIdleAnimation");
                break;
            case ObjectState.Run:
            case ObjectState.Charge:
                //need this one so the object will not just repeat the run animation while running
                if (objectState != ObjectState.Run && objectState != ObjectState.Charge)
                {
                    animatorWrapper.AddTriggerToQueue("EnterMovementAnimation");
                }
                break;
            case ObjectState.Attack:
                navMeshAgent.Stop();
                animatorWrapper.AddTriggerToQueue("EnterAttackAnimation");
                break;
            case ObjectState.Stun:
                break;
            case ObjectState.Special:
                navMeshAgent.Stop();
                animatorWrapper.AddTriggerToQueue("EnterAttackAnimation");
                break;
            case ObjectState.Die:
                //Maybe special case here ? to add the dead effect here and other stuffs.
                
                //Object die while moving, when respawned will have a weird position offset.
                //This is to fix that bug
                objectRenderer.OnParentObjectDie();
                break;
        }
        objectState = state;
    }

    protected virtual void AdditionalUpdateFunction()
    {
        //Make the special skill countdown correctly
        UpdateSpecialCountDown();
        //Regen the object's health.
        RegenHealth();
        //update animator wrapper to make animation run correctly
        animatorWrapper.DoUpdate();
        //update the renderer because of things....
        objectRenderer.DoUpdateRenderer();
    }

    protected virtual void ObjectIdle()
    {
        if (isHavingTargetPosition)
        {
            MoveToTargetPosition();
            return;
        }
        idleCountDown -= Time.deltaTime;
        if (idleCountDown <= 0f)
        {
            idleCountDown = GameConstant.idleCheckFrequency;
            if (objectManager.RequestTarget(this) != null)
            {
                ChargeAtObject(objectManager.RequestTarget(this), true);
            } else
            {
                if (isFollowingAlly)
                {
                    if (followingAlly != null && followingAlly.CanTargetObject())
                    {
                        if ((followingAlly.transform.position - transform.position).magnitude >= GameConstant.runningReachingDistance * 6)
                        {
                            SetTargetMovePosition(followingAlly.transform.position, true);
                        }
                    }
                    else
                    {
                        isFollowingAlly = false;
                    }
                }
            }
        }
    }

    [HideInInspector] //this to make sure the object follow other smoothly
    private int followAllyCountDown = 20; 

    protected virtual void ObjectRunning()
    {
        //TODO: I'm not very sure about this. This could hurt performance
        if (isFollowingAlly)
        {
            followAllyCountDown--;
            if (followAllyCountDown <= 0)
            {
                followAllyCountDown = 20;
                if ((followingAlly.transform.position - targetPosition).magnitude >= GameConstant.runningReachingDistance * 2) {
                    SetTargetMovePosition(followingAlly.transform.position, true);
                }
            }
        }
        //Old function. Working pretty well :/
        if ((transform.position - targetPosition).magnitude <= GameConstant.runningReachingDistance)
        {
            SetState(ObjectState.Idle);
        }
    }

    protected virtual void ObjectCharging()
    {
        //target changed mean target is die and is respawned as another object by the pool system.
        if (IsTargetChanged())
        {
            SetState(ObjectState.Idle);
            return;
        }
        if (IsTargetInRange())
        {
            StartAttackTarget();
            return;
        } 
        
        objectChargeCountdown--;
        if (objectChargeCountdown <= 0)
        {
            objectChargeCountdown = GameConstant.objectChargeCountdownValue;
            //set the target position again.
            targetPosition = targetObject.transform.position;
            navMeshAgent.SetDestination(targetPosition);
        }
    }

    protected virtual void ObjectAttack()
    {
        //it should be attack count up, shouldn't it ? LOL
        attackCountUp += Time.deltaTime;
        if (!isDamageDeal)
        {
            if (attackCountUp >= objectData.dealDamageTime)
            {
                DealDamageToTarget();
                isDamageDeal = true;
            }
        }
        else
        {
            if (attackCountUp >= objectData.attackDuration)
            {
                FinnishAttackTarget();
            }
        }
    }

    protected virtual void ObjectStun() { }

    protected virtual void ObjectSpecial() { }

    //overide for hero
    protected virtual void WhileObjectDie() { }

    //manager call - this function is called by manager or by player control
    //when hero is busy (like being stun, or attacking) it can not move to the target position instantly
    //instead, it just add the action to the queue
    //is local call: is this function called locally or externally
    public void SetTargetMovePosition(Vector3 _targetPosition, bool isLocalCall)
    {
        if (!isLocalCall)
        {
            isFollowingAlly = false;
        }
        targetPosition = _targetPosition;
        if (CanExecuteMoveOrder())
        {
            MoveToTargetPosition();
        } else
        {
            isHavingTargetPosition = true;
        }
    }

    //If not, the move order must be queued up.
    protected virtual bool CanExecuteMoveOrder()
    {
        return GetObjectState() == ObjectState.Run
            || GetObjectState() == ObjectState.Charge
            || GetObjectState() == ObjectState.Idle;
    }

    protected void MoveToTargetPosition()
    {
        navMeshAgent.Resume();
        navMeshAgent.SetDestination(targetPosition);
        isHavingTargetPosition = false;
        SetState(ObjectState.Run);
    }

    //manager call - this function is called by manager or by player control
    public void ChargeAtObject(BaseObject target, bool isLocalCall)
    {
        if (!isLocalCall)
        {
            isFollowingAlly = false;
        }
        if (target == null)
        {
            //just let it be idle
            SetState(ObjectState.Idle);
        }
        else
        {
            SetTargetObject(target);
            targetPosition = targetObject.transform.position;
            navMeshAgent.Resume();
            navMeshAgent.SetDestination(targetPosition);
            SetState(ObjectState.Charge);
        }
    }

    //Make object run after an ally.
    public void SetFollowAlly(BaseObject allyObject)
    {
        followingAlly = allyObject;
        SetTargetMovePosition(followingAlly.transform.position, true);
        isFollowingAlly = true;
    }

    protected void SetTargetObject(BaseObject _target)
    {
        targetObject = _target;
        targetID = _target.id;
    }

    protected bool IsTargetChanged()
    {
        return targetID != targetObject.id;    
    }

    protected bool IsTargetInRange()
    {
        if (Ultilities.GetDistanceBetween(gameObject, targetObject.gameObject) <= objectData.attackRange)
        //if ((transform.position - targetPosition).magnitude <= objectData.attackRange)
        {
            return true;
        }
        return false;
    }

    protected virtual void StartAttackTarget()
    {
        if (targetObject != null && !IsTargetChanged())
        {
            transform.LookAt(targetObject.transform.position);
            SetState(ObjectState.Attack);
        }
        else
        {
            SetState(ObjectState.Idle);
            return;
        }
        isDamageDeal = false;
        attackCountUp = 0;
    }

    //TODO recheck this function !!!!
    protected virtual void DealDamageToTarget()
    {
        if (targetObject != null && !IsTargetChanged())
        {
            projectileManager.CreateProjectile(GetProjectileType(), isEnemy, objectData.damange, transform.position, this, targetObject.transform.position, targetObject);
            //targetObject.ReceiveDamage(10, this);
        }
    }

    protected void FinnishAttackTarget()
    {
        isDamageDeal = false;
        //action cancle. If hero is attacking and the player want to cancel it.
        if (isHavingTargetPosition)
        {
            MoveToTargetPosition();
            return;
        }

        //The last check -> if enemy is attacking the cargo, while the hero is close, he should focus on the
        //hero instead. -> Might change later
        if (targetObject != null && targetObject.objectState != ObjectState.Die && !IsTargetChanged() && IsTargetInRange())
        {
            StartAttackTarget();
        }
        else
        {
            BaseObject requestedTarget = objectManager.RequestTarget(this);
            if (requestedTarget == null)
            {
                SetState(ObjectState.Idle);
            }
            else
            {
                ChargeAtObject(requestedTarget, true);
            }
        }
    }

    //Special skill of object. Usually use for hero but I might think of something clever later
    //Implement this to make sure if you CAN NOT activate the special, you WON'T
    public virtual bool ActiveSpecial()
    {
        isFollowingAlly = false;
        if (CanActiveSpecial())
        {
            objectData.currentSpecialCoolDown = 0;
            return true;
        }
        return false;
    }

    protected virtual bool CanActiveSpecial()
    {
        return objectData.currentSpecialCoolDown >= objectData.specialCoolDown;
    }

    protected virtual void UpdateSpecialCountDown()
    {
        if (objectData.currentSpecialCoolDown < objectData.specialCoolDown)
        {
            objectData.currentSpecialCoolDown += Time.deltaTime;
        }
    }

    //Passing the attacker in to check detail
    public virtual void ReceiveDamage(int damage, BaseObject attacker = null)
    {
        //If the object is attacked, stop regen health for 3 seconds.
        if (GetHealthRegenRate() > 0f)
        {
            healthRegenCountUp = - GameConstant.attackStopRegenTime * GetHealthRegenRate() * objectData.maxHealth / 100f;
        }
        //Complex calculation here later if I want the game to get complex
        ReduceHealth(damage);
    }

    protected virtual void ReduceHealth(int damage)
    {
        objectData.health -= damage;
        objectRenderer.UpdateHealthBar();
        if (objectData.health <= 0)
        {
            OnObjectDie();
        } 
    }

    public virtual void OnObjectDie()
    {
        SetState(ObjectState.Die);

        DeadEffect();
        objectManager.RemoveObject(this);
        KillObject();
    }

    protected virtual void DeadEffect()
    {
        for (int i = 0; i < 9; i++)
        {
            Corpse corpse = PrefabsManager.SpawnCorpse(GetCorpseType());
            corpse.Init(transform.position);
        }
    }

    protected virtual void KillObject()
    {
        //TODO Use fucking pools man
        //Destroy(gameObject);
        ReturnToPool();
    }

    //temp variable
    private float healthRegenCountUp = 0f;

    protected virtual void RegenHealth()
    {
        if (GetHealthRegenRate() <= 0 || objectState == ObjectState.Die || objectData.health >= objectData.maxHealth)
        {
            return;
        }
        healthRegenCountUp += Time.deltaTime * GetHealthRegenRate() * objectData.maxHealth / 100f;
        if (healthRegenCountUp >= 1)
        {
            objectData.health = Mathf.Min((int)healthRegenCountUp + objectData.health, objectData.maxHealth);
            healthRegenCountUp -= (int)healthRegenCountUp;
            objectRenderer.UpdateHealthBar();
        }
    }

    //percent of max health regen per second
    //1 mean 1% of health every second
    protected virtual float GetHealthRegenRate() {
        return 0f;
    }

    //tobe called by the renderer function. Hero will have this function off.
    public virtual bool AutoHideHealthBar() { return true; }

    public virtual bool CanTargetObject()
    {
        return GetObjectState() != ObjectState.Die;
    }
}

public enum ObjectState
{
    Idle, //standing in one place and not doing anything
    Run, //runnnnnn
    Charge, //running to attack a target
    Attack, //attack target
    Stun, //object is stunned and can't do anything
    Special, //is doing special spell
    Die //die 
}