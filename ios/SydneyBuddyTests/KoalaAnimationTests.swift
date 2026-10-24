import XCTest
@testable import SydneyBuddy

final class KoalaAnimationTests:XCTestCase {
    private var manifest:KoalaManifest {KoalaManifest(animations:[
        KoalaClip(id:"idle",fps:12,frames:36,playback:"loop",ending:"loop",recovery:"none",framePath:"",poster:""),
        KoalaClip(id:"clap",fps:12,frames:12,playback:"once",ending:"idle",recovery:"none",framePath:"",poster:""),
        KoalaClip(id:"map",fps:12,frames:36,playback:"once",ending:"hold",recovery:"reverse",framePath:"",poster:""),
        KoalaClip(id:"tired",fps:12,frames:36,playback:"once",ending:"hold",recovery:"hold",framePath:"",poster:"")])}
    func testOneEventPlaysOnceAndDuplicateTokensCannotRestartIt() {
        var player=KoalaPlayback(manifest);player.request(.clap,token:"one");player.advance()
        for _ in 0..<5{player.advance()};let frame=player.frame
        player.request(.clap,token:"one");XCTAssertEqual(frame,player.frame)
        for _ in 0..<10{player.advance()};XCTAssertEqual(.idle,player.action)
    }
    func testMapRecoversThroughExistingFramesInsteadOfTeleporting() {
        var player=KoalaPlayback(manifest);player.request(.map,token:"thinking");player.advance()
        for _ in 0..<10{player.advance()};XCTAssertEqual(11,player.frame)
        player.request(.idle,token:"reply");player.advance();XCTAssertTrue(player.reversing);XCTAssertEqual(10,player.frame)
        for _ in 0..<12{player.advance()};XCTAssertEqual(.idle,player.action)
    }
    func testLyingEndpointCannotSnapBackWithoutAnAuthoredRecovery() {
        var player=KoalaPlayback(manifest);player.request(.tired,token:"tired");player.advance()
        for _ in 0..<40{player.advance()};player.request(.idle,token:"rested")
        for _ in 0..<40{player.advance()};XCTAssertEqual(.tired,player.action);XCTAssertEqual(36,player.frame)
    }
    func testBundledManifestAndEveryNumberedFrameAreIncluded() throws {
        let manifest=try XCTUnwrap(KoalaManifest.load());XCTAssertEqual(10,manifest.animations.count)
        XCTAssertEqual(276,manifest.animations.reduce(0){$0+$1.frames})
        for clip in manifest.animations{for frame in 1...clip.frames {
            XCTAssertTrue(FileManager.default.fileExists(atPath:Bundle.main.resourceURL!.appendingPathComponent("koala/"+clip.path(frame)).path))
        }}
    }
}
