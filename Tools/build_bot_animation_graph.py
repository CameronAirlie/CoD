"""Build UAL locomotion with masked rifle poses; safe to run independently."""
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent
ASSETS=ROOT/"Assets"
BASE="project://Bots/Soldier/"

def build_graph():
    graph='AnimationGraphVersion=4\nDefaultStateId=1\n'
    for i,(name,type_,default) in enumerate([('MovementSpeed','Float',0),('HasTarget','Bool',0),('Shoot','Trigger',0),('Hit','Trigger',0),('Reload','Trigger',0),('SupportHandIK','Float',1)],1):
        graph+=f'Parameter={i}|{name}|{type_}|{default}|0|0\n'
    for i,name in enumerate(['Idle_Loop','Walk_Loop','Jog_Fwd_Loop','Sprint_Loop'],1):
        graph+=f'State={i}|UAL {name}|{name}|0|{i*180}|100|1|1|project://SourceModels/UAL1_Standard/{name}.plutoclip\n'
    for i,(a,b,op,value) in enumerate([(1,2,'Greater',.05),(2,1,'Less',.05),(2,3,'Greater',2.4),(3,2,'Less',2.4),(3,4,'Greater',5.8),(4,3,'Less',5.8)],1):
        graph+=f'Transition={i}|{a}|{b}|0.14|0|0.9\nCondition={i}|MovementSpeed|{op}|{value}\n'
    # Preserve UAL hips/legs; soften the seam into the weapon pose.
    graph+='BoneMask=1|Rifle Upper Body|0\nBoneMaskEntry=1|1|0.25|1\nBoneMaskEntry=1|2|0.75|1\nBoneMaskEntry=1|3|1|1\n'
    # No activation parameter: keep the weapon held even when HasTarget is false.
    graph+=f'Layer=1|Rifle Hold|{BASE}Rifle_Idle.plutoclip|Rifle_Idle|0|1|0|1|||1|0.12|0.12|1|1|1|\n'
    for i,(clip,activation,loop) in enumerate([('Aim','HasTarget',1),('Fire','Shoot',0),('Hit','Hit',0),('Reload','Reload',0)],2):
        graph+=f'Layer={i}|Rifle {clip}|{BASE}Rifle_{clip}.plutoclip|Rifle_{clip}|0|1|0|1||{activation}|1|0.04|0.10|{loop}|1|1|\n'
    return graph

if __name__ == "__main__":
    (ASSETS/"Bots/Soldier/Soldier.plutoanimgraph").write_text(build_graph(),encoding="utf8")
