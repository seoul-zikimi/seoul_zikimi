using UnityEditor;

/// <summary>
/// Cartoon FX Remaster(CFXR) 이펙트 텍스처 임포트 정책.
///
/// 모바일에서 DevicePerformanceTuner가 저사양 티어(≤3.2GB)·메모리 경고 시 QualitySettings.globalTextureMipmapLimit을
/// 1~2로 올린다. 그러면 먼지·연기처럼 원래 128~256px밖에 안 되는 이펙트 텍스처가 32~64px로 내려가 디졸브 노이즈가
/// 뭉개지고 '모자이크'처럼 보인다(2026-09-04 모바일 먼지 이슈). 이펙트 텍스처는 애초에 작아 메모리 이득도 없으니
/// 전역 밉맵 한도에서 제외한다. .meta에도 ignoreMipmapLimit: 1을 넣어 두었지만, 새로 추가되는 CFXR 텍스처나
/// 임포터 버전 업그레이드로 값이 초기화되는 경우를 위해 임포트 시점에 한 번 더 강제한다.
/// </summary>
public sealed class CfxrTextureImportPolicy : AssetPostprocessor
{
    private const string kCfxrRoot = "/Cartoon FX Remaster/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.Contains(kCfxrRoot)) return;
        var importer = (TextureImporter)assetImporter;
        if (importer.mipmapEnabled && !importer.ignoreMipmapLimit)
            importer.ignoreMipmapLimit = true;
    }
}
