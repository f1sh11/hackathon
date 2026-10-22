from pathlib import Path
import hashlib
import plistlib
root=Path(__file__).resolve().parents[1]/'ios'
def uid(s):return hashlib.sha1(s.encode()).hexdigest()[:24].upper()
objects={}
def obj(label,body):
 ident=uid(label);objects[ident]=body;return ident
source_refs=[];build_refs=[]
for p in sorted((root/'SydneyBuddy').glob('*.swift')):
 f=obj(p.name,{'isa':'PBXFileReference','lastKnownFileType':'sourcecode.swift','path':p.name,'sourceTree':'<group>'})
 source_refs.append(f);build_refs.append(obj('build'+p.name,{'isa':'PBXBuildFile','fileRef':f}))
configfile=obj('base',{'isa':'PBXFileReference','lastKnownFileType':'text.xcconfig','path':'Config/Base.xcconfig','sourceTree':'<group>'})
plist=obj('plist',{'isa':'PBXFileReference','lastKnownFileType':'text.plist.xml','path':'Config/Info.plist','sourceTree':'<group>'})
sourcegroup=obj('sourcegroup',{'isa':'PBXGroup','children':source_refs,'path':'SydneyBuddy','sourceTree':'<group>'})
product=obj('app',{'isa':'PBXFileReference','explicitFileType':'wrapper.application','path':'SydneyBuddy.app','sourceTree':'BUILT_PRODUCTS_DIR'})
products=obj('products',{'isa':'PBXGroup','children':[product],'name':'Products','sourceTree':'<group>'})
main=obj('main',{'isa':'PBXGroup','children':[sourcegroup,configfile,plist,products],'sourceTree':'<group>'})
pkg=obj('googlepackage',{'isa':'XCRemoteSwiftPackageReference','repositoryURL':'https://github.com/googlemaps/ios-maps-sdk','requirement':{'kind':'exactVersion','version':'11.2.0'}})
prod=obj('googleproduct',{'isa':'XCSwiftPackageProductDependency','package':pkg,'productName':'GoogleMaps'})
framework=obj('googlebuild',{'isa':'PBXBuildFile','productRef':prod})
sources=obj('sources',{'isa':'PBXSourcesBuildPhase','buildActionMask':2147483647,'files':build_refs,'runOnlyForDeploymentPostprocessing':0})
frameworks=obj('frameworks',{'isa':'PBXFrameworksBuildPhase','buildActionMask':2147483647,'files':[framework],'runOnlyForDeploymentPostprocessing':0})
resources=obj('resources',{'isa':'PBXResourcesBuildPhase','buildActionMask':2147483647,'files':[],'runOnlyForDeploymentPostprocessing':0})
def configs(prefix,base,settings):
 configs=[]
 for name in ['Debug','Release']:
  d={'isa':'XCBuildConfiguration','buildSettings':dict(settings,SWIFT_OPTIMIZATION_LEVEL='-Onone' if name=='Debug' else '-O'),'name':name}
  if base:d['baseConfigurationReference']=base
  configs.append(obj(prefix+name,d))
 return obj(prefix+'list',{'isa':'XCConfigurationList','buildConfigurations':configs,'defaultConfigurationIsVisible':0,'defaultConfigurationName':'Release'})
projconfig=configs('project',None,{'SDKROOT':'iphoneos','IPHONEOS_DEPLOYMENT_TARGET':'17.0','CLANG_ENABLE_MODULES':'YES','SWIFT_VERSION':'5.0'})
targetconfig=configs('target',configfile,{'PRODUCT_BUNDLE_IDENTIFIER':'com.sydneybuddy.travel','PRODUCT_NAME':'$(TARGET_NAME)','INFOPLIST_FILE':'Config/Info.plist','TARGETED_DEVICE_FAMILY':'1','CODE_SIGN_STYLE':'Automatic','MARKETING_VERSION':'0.1.0','CURRENT_PROJECT_VERSION':'1','GENERATE_INFOPLIST_FILE':'NO','OTHER_LDFLAGS':['$(inherited)','-ObjC'],'LD_RUNPATH_SEARCH_PATHS':['$(inherited)','@executable_path/Frameworks'],'SWIFT_EMIT_LOC_STRINGS':'YES'})
target=obj('target',{'isa':'PBXNativeTarget','buildConfigurationList':targetconfig,'buildPhases':[sources,frameworks,resources],'buildRules':[],'dependencies':[],'name':'SydneyBuddy','packageProductDependencies':[prod],'productName':'SydneyBuddy','productReference':product,'productType':'com.apple.product-type.application'})
project=obj('project',{'isa':'PBXProject','attributes':{'LastUpgradeCheck':'2700'},'buildConfigurationList':projconfig,'compatibilityVersion':'Xcode 14.0','developmentRegion':'zh-Hans','hasScannedForEncodings':0,'knownRegions':['en','zh-Hans','Base'],'mainGroup':main,'packageReferences':[pkg],'productRefGroup':products,'projectDirPath':'','projectRoot':'','targets':[target]})
def encode(v,indent=0):
 if isinstance(v,dict):return '{\n'+''.join('  '*(indent+1)+k+' = '+encode(val,indent+1)+';\n' for k,val in v.items())+'  '*indent+'}'
 if isinstance(v,list):return '('+', '.join(encode(x,indent) for x in v)+')'
 if isinstance(v,int):return str(v)
 import json
 return json.dumps(v)
p=root/'SydneyBuddy.xcodeproj';p.mkdir(exist_ok=True)
(p/'project.pbxproj').write_text('// !$*UTF8*$!\n'+encode({'archiveVersion':1,'classes':{},'objectVersion':56,'objects':objects,'rootObject':project})+'\n')
info={'CFBundleDevelopmentRegion':'zh-Hans','CFBundleDisplayName':'悉尼 Buddy','CFBundleExecutable':'$(EXECUTABLE_NAME)','CFBundleIdentifier':'$(PRODUCT_BUNDLE_IDENTIFIER)','CFBundleInfoDictionaryVersion':'6.0','CFBundleName':'$(PRODUCT_NAME)','CFBundlePackageType':'APPL','CFBundleShortVersionString':'$(MARKETING_VERSION)','CFBundleVersion':'$(CURRENT_PROJECT_VERSION)','LSRequiresIPhoneOS':True,'UILaunchScreen':{},'UISupportedInterfaceOrientations':['UIInterfaceOrientationPortrait'],'GoogleMapsAPIKey':'$(GOOGLE_MAPS_API_KEY)','LSApplicationQueriesSchemes':['googlechromes','comgooglemaps'],'NSAppTransportSecurity':{'NSAllowsLocalNetworking':True,'NSExceptionDomains':{'127.0.0.1':{'NSExceptionAllowsInsecureHTTPLoads':True}}}}
(root/'Config/Info.plist').write_bytes(plistlib.dumps(info))
print(p)
