"""Restore source item-layer transition durations without regenerating the controller."""
from pathlib import Path
import re
import json

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path(r'D:\vrc-wine-fox\Assets\酒狐合并.controller')
TARGET = ROOT / 'Assets/WineFoxItems/WineFoxItemsFX.controller'

def documents(path):
    text = path.read_text(encoding='utf-8')
    return text, {int(m[1]): m[2] for m in re.finditer(r'^--- !u!\d+ &(-?\d+)\n(.*?)(?=^---|\Z)', text, re.M | re.S)}

def name(body):
    value = re.search(r'^  m_Name: (.*)$', body, re.M)[1]
    return json.loads(value) if value.startswith('"') else value

def refs(body, field):
    match = re.search(r'^  '+field+r':\n((?:  - .*\n)*)', body, re.M)
    return [int(v) for v in re.findall(r'fileID: (-?\d+)', match[1])] if match else []

def scalar(body, field):
    return re.search(r'^  '+field+r': (.*)$', body, re.M)[1]

def states(docs, layer):
    result = {}
    root = next(i for i,b in docs.items() if b.startswith('AnimatorStateMachine:') and name(b)==layer)
    def visit(i, path):
        body = docs[i]
        children = body.split('  m_ChildStates:',1)[1].split('  m_ChildStateMachines:',1)[0]
        for ref in re.findall(r'm_State: \{fileID: (-?\d+)\}', children):
            ref = int(ref); result[path+name(docs[ref])] = ref
        children = body.split('  m_ChildStateMachines:',1)[1].split('  m_AnyStateTransitions:',1)[0]
        for ref in re.findall(r'm_StateMachine: \{fileID: (-?\d+)\}', children):
            ref = int(ref); visit(ref,path+name(docs[ref])+'/')
    visit(root,'')
    return result

original_text, source = documents(SOURCE)
target_text, target = documents(TARGET)
source_states = states(source,'物品')
target_states = states(target,'物品')
source_paths = {i:p for p,i in source_states.items()}
target_paths = {i:p for p,i in target_states.items()}
manifest = []
changed = []
for path, sid in source_states.items():
    tid = target_states[path]
    original_refs = refs(source[sid], 'm_Transitions')
    current_refs = refs(target[tid], 'm_Transitions')
    assert len(current_refs)>=len(original_refs), path
    for index, (sref,tref) in enumerate(zip(original_refs,current_refs)):
        sb, tb = source[sref], target[tref]
        sd = int(re.search(r'm_DstState: \{fileID: (-?\d+)\}',sb)[1])
        td = int(re.search(r'm_DstState: \{fileID: (-?\d+)\}',tb)[1])
        assert source_paths.get(sd)==target_paths.get(td), (path,index,'destination')
        assert scalar(sb,'m_IsExit')==scalar(tb,'m_IsExit'), (path,index,'exit')
        source_conditions = sb.split('  m_Conditions:',1)[1].split('  m_DstStateMachine:',1)[0]
        target_conditions = tb.split('  m_Conditions:',1)[1].split('  m_DstStateMachine:',1)[0]
        for old,new in {'item':'Item','trigger':'Trigger','itemhit':'Hit','eating':'Eating'}.items():
            source_conditions = re.sub(r'(m_ConditionEvent: )'+old+r'(?=\n)',r'\1WineFoxItems/'+new,source_conditions)
        assert source_conditions == target_conditions, (path,index,'conditions')
        duration = scalar(sb,'m_TransitionDuration')
        old = scalar(tb,'m_TransitionDuration')
        manifest.append(dict(state=path,index=index,duration=float(duration)))
        if float(duration) > 0:
            changed.append(f'{path} transition {index}: source duration {duration} s; restored and verified')
        if old != duration:
            patched = re.sub(r'(?m)^(  m_TransitionDuration: ).*$',lambda m:m[1]+duration,tb)
            target_text=target_text.replace(tb,patched,1)
TARGET.write_text(target_text,encoding='utf-8',newline='\n')
(ROOT/'Assets/WineFoxItems/Editor/OriginalItemTransitions.json').write_text(json.dumps({'transitions':manifest},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
report='Restored source durations; validated source state, destination and conditions.\n'+'\n'.join(changed)+'\n'
(ROOT/'Artifacts/transition-repair.txt').write_text(report,encoding='utf-8')
print(report)
