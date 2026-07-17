using System.Collections;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// MenuVideoController — quản lý VideoPlayer background của MenuScene.
/// SRP: chỉ chịu trách nhiệm điều khiển video (swap clip, play, wait).
///
/// Gắn vào cùng GameObject có VideoPlayer, hoặc truyền reference qua Inspector.
/// </summary>
public class MenuVideoController : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;

    private void Awake()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Swap clip và phát ngay lập tức (không loop).
    /// Trả về coroutine — yield bên ngoài để chờ video kết thúc.
    /// </summary>
    public IEnumerator PlayAndWait(VideoClip clip)
    {
        if (videoPlayer == null || clip == null) yield break;

        videoPlayer.clip      = clip;
        videoPlayer.isLooping = false;
        videoPlayer.Stop();
        videoPlayer.Play();

        // Chờ cho đến khi video kết thúc
        yield return new WaitUntil(() =>
            videoPlayer.isPrepared &&
            !videoPlayer.isPlaying &&
            videoPlayer.time > 0);
    }

    /// <summary>Đổi clip và phát loop (video background bình thường).</summary>
    public void PlayLoop(VideoClip clip)
    {
        if (videoPlayer == null || clip == null) return;
        videoPlayer.clip      = clip;
        videoPlayer.isLooping = true;
        videoPlayer.Stop();
        videoPlayer.Play();
    }

    /// <summary>Dừng video hiện tại.</summary>
    public void Stop() => videoPlayer?.Stop();
}
