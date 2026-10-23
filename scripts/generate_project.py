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
navpkg=obj('ferrostarpackage',{'isa':'XCRemoteSwiftPackageReference','repositoryURL':'https://github.com/stadiamaps/ferrostar','requirement':{'kind':'exactVersion','version':'0.59.0'}})
navprod=obj('ferrostarproduct',{'isa':'XCSwiftPackageProductDependency','package':navpkg,'productName':'FerrostarCore'})
navframework=obj('ferrostarbuild',{'isa':'PBXBuildFile','productRef':navprod})
sources=obj('sources',{'isa':'PBXSourcesBuildPhase','buildActionMask':2147483647,'files':build_refs,'runOnlyForDeploymentPostprocessing':0})
frameworks=obj('frameworks',{'isa':'PBXFrameworksBuildPhase','buildActionMask':2147483647,'files':[framework,navframework],'runOnlyForDeploymentPostprocessing':0})
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
target=obj('target',{'isa':'PBXNativeTarget','buildConfigurationList':targetconfig,'buildPhases':[sources,frameworks,resources],'buildRules':[],'dependencies':[],'name':'SydneyBuddy','packageProductDependencies':[prod,navprod],'productName':'SydneyBuddy','productReference':product,'productType':'com.apple.product-type.application'})
# XCTest host verifies itinerary progress and the real Ferrostar parser/simulator without network calls.
testref=obj('navigationtests',{'isa':'PBXFileReference','lastKnownFileType':'sourcecode.swift','path':'SydneyBuddyTests/NavigationTests.swift','sourceTree':'<group>'})
fixture=obj('navigationfixture',{'isa':'PBXFileReference','lastKnownFileType':'text.json','path':'../tests/fixtures/walking-sydney.json','sourceTree':'<group>'})
objects[main]['children'] += [testref,fixture]
testproduct=obj('testproduct',{'isa':'PBXFileReference','explicitFileType':'wrapper.cfbundle','path':'SydneyBuddyTests.xctest','sourceTree':'BUILT_PRODUCTS_DIR'})
objects[products]['children'].append(testproduct)
testsources=obj('testsources',{'isa':'PBXSourcesBuildPhase','buildActionMask':2147483647,'files':[obj('buildnavigationtests',{'isa':'PBXBuildFile','fileRef':testref})],'runOnlyForDeploymentPostprocessing':0})
testresources=obj('testresources',{'isa':'PBXResourcesBuildPhase','buildActionMask':2147483647,'files':[obj('buildnavigationfixture',{'isa':'PBXBuildFile','fileRef':fixture})],'runOnlyForDeploymentPostprocessing':0})
testframeworks=obj('testframeworks',{'isa':'PBXFrameworksBuildPhase','buildActionMask':2147483647,'files':[obj('testferrostarbuild',{'isa':'PBXBuildFile','productRef':navprod})],'runOnlyForDeploymentPostprocessing':0})
testconfig=configs('tests',None,{'PRODUCT_NAME':'$(TARGET_NAME)','PRODUCT_BUNDLE_IDENTIFIER':'com.sydneybuddy.travel.tests','GENERATE_INFOPLIST_FILE':'YES','SDKROOT':'iphonesimulator','IPHONEOS_DEPLOYMENT_TARGET':'17.0','SWIFT_VERSION':'5.0','TEST_HOST':'$(BUILT_PRODUCTS_DIR)/SydneyBuddy.app/SydneyBuddy','BUNDLE_LOADER':'$(TEST_HOST)','TARGETED_DEVICE_FAMILY':'1','CODE_SIGN_STYLE':'Automatic'})
objects[uid('targetDebug')]['buildSettings']['ENABLE_TESTABILITY']='YES'
objects[uid('projectDebug')]['buildSettings']['ONLY_ACTIVE_ARCH']='YES'
proxy=obj('testproxy',{'isa':'PBXContainerItemProxy','containerPortal':uid('project'),'proxyType':1,'remoteGlobalIDString':target,'remoteInfo':'SydneyBuddy'})
dependency=obj('testdependency',{'isa':'PBXTargetDependency','target':target,'targetProxy':proxy})
testtarget=obj('testtarget',{'isa':'PBXNativeTarget','name':'SydneyBuddyTests','productName':'SydneyBuddyTests','productType':'com.apple.product-type.bundle.unit-test','productReference':testproduct,'buildConfigurationList':testconfig,'buildPhases':[testsources,testframeworks,testresources],'buildRules':[],'dependencies':[dependency],'packageProductDependencies':[navprod]})
project=obj('project',{'isa':'PBXProject','attributes':{'LastUpgradeCheck':'2700'},'buildConfigurationList':projconfig,'compatibilityVersion':'Xcode 14.0','developmentRegion':'zh-Hans','hasScannedForEncodings':0,'knownRegions':['en','zh-Hans','Base'],'mainGroup':main,'packageReferences':[pkg,navpkg],'productRefGroup':products,'projectDirPath':'','projectRoot':'','targets':[target,testtarget]})
def encode(v,indent=0):
 if isinstance(v,dict):return '{\n'+''.join('  '*(indent+1)+k+' = '+encode(val,indent+1)+';\n' for k,val in v.items())+'  '*indent+'}'
 if isinstance(v,list):return '('+', '.join(encode(x,indent) for x in v)+')'
 if isinstance(v,int):return str(v)
 import json
 return json.dumps(v)
p=root/'SydneyBuddy.xcodeproj';p.mkdir(exist_ok=True)
(p/'project.pbxproj').write_text('// !$*UTF8*$!\n'+encode({'archiveVersion':1,'classes':{},'objectVersion':56,'objects':objects,'rootObject':project})+'\n')
info={'CFBundleDevelopmentRegion':'zh-Hans','CFBundleDisplayName':'悉尼 Buddy','CFBundleExecutable':'$(EXECUTABLE_NAME)','CFBundleIdentifier':'$(PRODUCT_BUNDLE_IDENTIFIER)','CFBundleInfoDictionaryVersion':'6.0','CFBundleName':'$(PRODUCT_NAME)','CFBundlePackageType':'APPL','CFBundleShortVersionString':'$(MARKETING_VERSION)','CFBundleVersion':'$(CURRENT_PROJECT_VERSION)','LSRequiresIPhoneOS':True,'UILaunchScreen':{},'UISupportedInterfaceOrientations':['UIInterfaceOrientationPortrait'],'GoogleMapsAPIKey':'$(GOOGLE_MAPS_API_KEY)','LSApplicationQueriesSchemes':['googlechromes','comgooglemaps'],'NSLocationWhenInUseUsageDescription':'用于步行导航、显示当前位置与到站提示。模拟导航不使用设备定位。','NSAppTransportSecurity':{'NSAllowsLocalNetworking':True,'NSExceptionDomains':{'127.0.0.1':{'NSExceptionAllowsInsecureHTTPLoads':True}}}}
(root/'Config/Info.plist').write_bytes(plistlib.dumps(info))
print(p)

schemes=root/'SydneyBuddy.xcodeproj/xcshareddata/xcschemes';schemes.mkdir(parents=True,exist_ok=True)
def ref(target_id,name):return f'<BuildableReference BuildableIdentifier="primary" BlueprintIdentifier="{target_id}" BuildableName="{name}" BlueprintName="{name.split(".")[0]}" ReferencedContainer="container:SydneyBuddy.xcodeproj"/>'
(schemes/'SydneyBuddy.xcscheme').write_text(f'''<?xml version="1.0" encoding="UTF-8"?>
<Scheme LastUpgradeVersion="2700" version="1.3">
<BuildAction parallelizeBuildables="YES" buildImplicitDependencies="YES"><BuildActionEntries><BuildActionEntry buildForTesting="YES" buildForRunning="YES" buildForProfiling="YES" buildForArchiving="YES" buildForAnalyzing="YES">{ref(target,'SydneyBuddy.app')}</BuildActionEntry></BuildActionEntries></BuildAction>
<TestAction buildConfiguration="Debug" selectedDebuggerIdentifier="Xcode.DebuggerFoundation.Debugger.LLDB" selectedLauncherIdentifier="Xcode.IDEFoundation.Launcher.LLDB" shouldUseLaunchSchemeArgsEnv="YES"><Testables><TestableReference skipped="NO">{ref(testtarget,'SydneyBuddyTests.xctest')}</TestableReference></Testables></TestAction>
<LaunchAction buildConfiguration="Debug" selectedDebuggerIdentifier="Xcode.DebuggerFoundation.Debugger.LLDB" selectedLauncherIdentifier="Xcode.IDEFoundation.Launcher.LLDB" launchStyle="0" useCustomWorkingDirectory="NO" ignoresPersistentStateOnLaunch="NO" debugDocumentVersioning="YES" debugServiceExtension="internal" allowLocationSimulation="YES"><BuildableProductRunnable runnableDebuggingMode="0">{ref(target,'SydneyBuddy.app')}</BuildableProductRunnable></LaunchAction>
<ProfileAction buildConfiguration="Release" shouldUseLaunchSchemeArgsEnv="YES" savedToolIdentifier="" useCustomWorkingDirectory="NO" debugDocumentVersioning="YES"><BuildableProductRunnable runnableDebuggingMode="0">{ref(target,'SydneyBuddy.app')}</BuildableProductRunnable></ProfileAction>
<AnalyzeAction buildConfiguration="Debug"/><ArchiveAction buildConfiguration="Release" revealArchiveInOrganizer="YES"/>
</Scheme>''')
