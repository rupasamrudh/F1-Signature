import re

scene_path = "Assets/Scenes/Track_F1Signature.unity"
with open(scene_path, "r", encoding="utf-8") as f:
    content = f.read()

# 1. Update RenderSettings
content = re.sub(r'm_HaloStrength:\s*[\d\.]+', 'm_HaloStrength: 0', content, count=1)
content = re.sub(r'm_FlareStrength:\s*[\d\.]+', 'm_FlareStrength: 0', content, count=1)

# 2. Update Directional Light (intensity: 1)
# Light is &410087040
content = re.sub(
    r'(--- !u!108 &410087040[\s\S]*?m_Intensity:\s*)[\d\.]+',
    r'\g<1>1',
    content,
    count=1
)

# 3. Update Camera Transform &330585546
cam_target = r'''--- !u!4 &330585546
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 330585543}
  serializedVersion: 2
  m_LocalRotation: {x: 0.0688, y: -0.7899, z: 0.0531, w: 0.607}
  m_LocalPosition: {x: 399.57, y: 42.5, z: -114.35}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 10, y: -104.92, z: 0}'''

content = re.sub(
    r'--- !u!4 &330585546[\s\S]*?m_LocalEulerAnglesHint:[^\n]+',
    cam_target,
    content,
    count=1
)

# 4. Update Car Transform &1823367134
car_transform_target = r'''--- !u!4 &1823367134
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1823367131}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: -0.792928, z: 0, w: 0.609314}
  m_LocalPosition: {x: 390.87, y: 40, z: -116.67}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children:
  - {fileID: 394330153}
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: -104.92, z: 0}'''

content = re.sub(
    r'--- !u!4 &1823367134[\s\S]*?m_LocalEulerAnglesHint:[^\n]+',
    car_transform_target,
    content,
    count=1
)

# 5. Update CarController &1823367132 paramsVersion: 3, brakeBiasFront: 0.68
content = re.sub(
    r'(--- !u!114 &1823367132[\s\S]*?paramsVersion:\s*)\d+',
    r'\g<1>3',
    content,
    count=1
)
content = re.sub(
    r'(--- !u!114 &1823367132[\s\S]*?brakeBiasFront:\s*)[\d\.]+',
    r'\g<1>0.68',
    content,
    count=1
)

# 6. Update CarInput &1823367133 inputParamsVersion: 1, maxTiltDeg: 40
content = re.sub(
    r'(--- !u!114 &1823367133[\s\S]*?inputParamsVersion:\s*)\d+',
    r'\g<1>1',
    content,
    count=1
)
content = re.sub(
    r'(--- !u!114 &1823367133[\s\S]*?maxTiltDeg:\s*)[\d\.]+',
    r'\g<1>40',
    content,
    count=1
)

# 7. Remove MarkerManager GameObjects & components (428193590, 428193591, 428193592)
content = re.sub(
    r'--- !u!1 &428193590[\s\S]*?(?=--- !u!1|\Z)',
    '',
    content,
    count=1
)

# 8. Remove Plane GameObjects & components (603387840, 603387841, 603387842, 603387843, 603387844)
content = re.sub(
    r'--- !u!1 &603387840[\s\S]*?(?=--- !u!1 &607356447)',
    '',
    content,
    count=1
)

# 9. Update SceneRoots: remove 603387844 and 428193591
content = re.sub(r'\s*-\s*\{fileID:\s*603387844\}', '', content)
content = re.sub(r'\s*-\s*\{fileID:\s*428193591\}', '', content)

with open(scene_path, "w", encoding="utf-8") as f:
    f.write(content)

print("Scene fixed successfully!")

