using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using DG.Tweening;
using Newtonsoft.Json.Linq;
using Proyecto26;
using RSG;
using UniRx.Async;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

public class GamePreparationScreen : Screen, ScreenChangeListener
{
    public const string Id = "GamePreparation";

    [GetComponent] public AudioSource previewAudioSource;

    [GetComponentInChildrenName] public DepthCover cover;
    public Text bestPerformanceDescriptionText;
    public PerformanceWidget bestPerformanceWidget;

    public GradientMeshEffect startButtonGradient;
    public Text startButtonText;

    public RankingsTab rankingsTab;
    public RatingTab ratingTab;

    public GameObject gameplayIcon;
    public GameObject settingsIcon;

    public RadioGroup practiceModeToggle;

    public InteractableMonoBehavior levelNoteOffsetCalibrateButton;
    public InputField levelNoteOffsetTextField;

    public RadioGroup earlyHitSoundsToggle;
    public RadioGroup largerHitboxesToggle;
    public RadioGroup earlyLateIndicatorsToggle;
    public CaretSelect noteSizeCaretSelect;
    public CaretSelect horizontalMarginCaretSelect;
    public CaretSelect verticalMarginCaretSelect;
    public InputField baseNoteOffsetTextField;
    public InputField headsetNoteOffsetTextfield;

    public CaretSelect storyboardEffectsCaretSelect;
    public RadioGroup lowerResolutionToggle;
    public RadioGroup showBoundariesToggle;
    public CaretSelect coverOpacityCaretSelect;
    public InputField ringColorTextField;
    public InputField fillColorClickUpTextField;
    public InputField fillColorClickDownTextfield;
    public InputField fillColorDragUpTextField;
    public InputField fillColorDragDownTextfield;
    public InputField fillColorHoldUpTextField;
    public InputField fillColorHoldDownTextfield;
    public InputField fillColorLongHoldUpTextField;
    public InputField fillColorLongHoldDownTextfield;
    public InputField fillColorFlickUpTextField;
    public InputField fillColorFlickDownTextfield;

    public RadioGroup displayProfilerToggle;
    public RadioGroup displayNoteIdsToggle;

    private DateTime asyncRequestsToken;
    private Sprite coverSprite;
    private AssetLoader previewAudioClip;

    public Level Level { get; set; }

    public override string GetId() => Id;

    private bool willStart;

    public override void OnScreenInitialized()
    {
        base.OnScreenInitialized();
        LoadSettings();
        Context.ScreenManager.AddHandler(this);
        levelNoteOffsetCalibrateButton.onPointerClick.AddListener(_ =>
        {
            Context.WillCalibrate = true;
            OnStartButton();
        });
    }

    public override void OnScreenBecameActive()
    {
        base.OnScreenBecameActive();
        asyncRequestsToken = DateTime.Now;

        if (Context.SelectedLevel == null)
        {
            Debug.LogWarning("Context.SelectedLevel is null");
            return;
        }

        LoopAudioPlayer.Instance.FadeOutLoopPlayer();
        ProfileWidget.Instance.Enter();

        var needReload = Level != Context.SelectedLevel;
        Level = Context.SelectedLevel;

        rankingsTab.UpdateRankings(Level.Id, Context.SelectedDifficulty.Id);
        ratingTab.UpdateLevelRating(Level.Id);
        Context.LevelManager.OnLevelMetaUpdated.AddListener(OnLevelMetaUpdated);
        Context.OnlinePlayer.OnLevelBestPerformanceUpdated.AddListener(OnLevelBestPerformanceUpdated);

        var localVersion = Level.Meta.version;
        Context.LevelManager.FetchLevelMeta(Level.Id, true).Then(it =>
        {
            print($"Remote version: {it.version}, local version: {localVersion}");
            if (it.version > Level.Meta.version)
            {
                // Ask the user to update
                var dialog = Dialog.Instantiate();
                dialog.Message = "This level is outdated.\nWould you like to update now?";
                dialog.UsePositiveButton = true;
                dialog.UseNegativeButton = true;
                dialog.OnPositiveButtonClicked = _ =>
                {
                    DownloadAndUnpackLevel();
                    dialog.Close();
                };
                dialog.Open();
            }
        });

        LoadLevelPerformance();
        LoadLevelSettings();
        LoadCover(needReload);
        LoadPreview(needReload);

        UpdateTopMenu();
        UpdateStartButton();
    }

    private void UpdateTopMenu()
    {
        gameplayIcon.SetActive(Level.IsLocal);
        settingsIcon.SetActive(Level.IsLocal);
    }

    private void UpdateStartButton()
    {
        if (Level.IsLocal)
        {
            startButtonGradient.SetGradient(
                Context.LocalPlayer.PlayRanked
                    ? new ColorGradient("#12D8FA".ToColor(), "#A6FFCB".ToColor(), -45)
                    : new ColorGradient("#F953C6".ToColor(), "#B91D73".ToColor(), 135)
            );
            startButtonText.text = Context.LocalPlayer.PlayRanked ? "Start!" : "Practice!";
        }
        else
        {
            startButtonGradient.SetGradient(
                new ColorGradient("#476ADC".ToColor(), "#9CAFEC".ToColor(), -45)
            );
            startButtonText.text = "Download!";
        }
    }

    private void OnLevelMetaUpdated(Level level)
    {
        if (level != Level) return;
        Toast.Enqueue(Toast.Status.Success, "Level metadata synchronized.");
        Context.OnSelectedLevelChanged.Invoke(Context.SelectedLevel);
    }

    public async void LoadCover(bool load)
    {
        if (load)
        {
            string path;
            if (Level.IsLocal)
            {
                path = "file://" + Level.Path + Level.Meta.background.path;
            }
            else
            {
                path = Level.Meta.background.path.WithImageCdn().WithSizeParam(1920, 1080);
            }

            var token = asyncRequestsToken;
            Sprite sprite;
            using (var request = UnityWebRequestTexture.GetTexture(path))
            {
                await request.SendWebRequest();
                if (request.isNetworkError || request.isHttpError)
                {
                    Debug.LogError($"Failed to download cover from {path}");
                    Debug.LogError(request.error);
                    return;
                }

                sprite = DownloadHandlerTexture.GetContent(request).CreateSprite();
            }

            if (asyncRequestsToken != token)
            {
                Destroy(sprite);
                return;
            }

            if (State == ScreenState.Active)
            {
                cover.OnCoverLoaded(sprite);
                coverSprite = sprite;
            }
        }
        else
        {
            cover.OnCoverLoaded(null);
        }
    }

    public async void LoadPreview(bool load)
    {
        if (load)
        {
            string path;
            if (Level.IsLocal)
            {
                path = "file://" + Level.Path + Level.Meta.music_preview.path;
            }
            else
            {
                path = Level.Meta.music_preview.path;
            }
            
            // Unload the current
            if (previewAudioClip != null)
            {
                previewAudioSource.clip = null;
                previewAudioClip.UnloadAudioClip();
                previewAudioClip = null;
            }

            // Load
            var token = asyncRequestsToken;
            var loader = new AssetLoader(path);
            await loader.LoadAudioClip();
            if (loader.Error != null)
            {
                Debug.LogError($"Failed to download preview from {path}");
                Debug.LogError(loader.Error);
                return;
            }

            if (asyncRequestsToken != token)
            {
                Destroy(loader.AudioClip);
                return;
            }

            if (State == ScreenState.Active)
            {
                previewAudioSource.clip = loader.AudioClip;
                previewAudioClip = loader;
            }
        }

        previewAudioSource.volume = 0;
        previewAudioSource.DOKill();
        previewAudioSource.DOFade(1, 1f).SetEase(Ease.Linear);
        previewAudioSource.loop = true;
        previewAudioSource.Play();
    }

    public void LoadLevelPerformance()
    {
        bestPerformanceDescriptionText.text =
            Context.LocalPlayer.PlayRanked ? "BEST PERFORMANCE" : "BEST PERFORMANCE (PRACTICE)";
        if (!Context.LocalPlayer.HasPerformance(Context.SelectedLevel.Id, Context.SelectedDifficulty.Id,
            Context.LocalPlayer.PlayRanked))
        {
            bestPerformanceWidget.SetModel(new LocalPlayer.Performance()); // 0
        }
        else
        {
            var performance = Context.LocalPlayer.GetBestPerformance(Context.SelectedLevel.Id,
                Context.SelectedDifficulty.Id,
                Context.LocalPlayer.PlayRanked);
            bestPerformanceWidget.SetModel(performance);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(bestPerformanceDescriptionText.transform as RectTransform);
    }

    public void OnLevelBestPerformanceUpdated(string levelId)
    {
        if (levelId != Level.Id) return;
        LoadLevelPerformance();
        Toast.Next(Toast.Status.Success, "Best performance synchronized.");
    }

    public void LoadLevelSettings()
    {
        var lp = Context.LocalPlayer;
        levelNoteOffsetTextField.SetTextWithoutNotify(
            lp.GetLevelNoteOffset(Context.SelectedLevel.Id)
                .ToString(CultureInfo.InvariantCulture));
        levelNoteOffsetTextField.onEndEdit.AddListener(FloatSettingHandler(levelNoteOffsetTextField,
            () => 0, value => lp.SetLevelNoteOffset(Context.SelectedLevel.Id, value)));
    }

    public override void OnScreenDestroyed()
    {
        base.OnScreenDestroyed();
        cover.image.color = Color.black;
        Context.ScreenManager.RemoveHandler(this);
    }

    public override void OnScreenBecameInactive()
    {
        base.OnScreenBecameInactive();
        Level = null;
        Context.LevelManager.OnLevelMetaUpdated.RemoveListener(OnLevelMetaUpdated);
        Context.OnlinePlayer.OnLevelBestPerformanceUpdated.RemoveListener(OnLevelBestPerformanceUpdated);

        asyncRequestsToken = DateTime.Now;
        previewAudioSource.DOFade(0, 1f).SetEase(Ease.Linear).onComplete = () => { previewAudioSource.Stop(); };
        if (!willStart) LoopAudioPlayer.Instance.FadeInLoopPlayer();
    }

    public void OnScreenChangeStarted(Screen @from, Screen to) => Expression.Empty();

    public void OnScreenChangeFinished(Screen from, Screen to)
    {
        if (from.GetId() == Id && to.GetId() != ProfileScreen.Id)
        {
            // Unload resources
            UnloadPreviewAudioClip();
            UnloadCoverSprite();
        }
    }

    private void UnloadPreviewAudioClip()
    {
        if (previewAudioClip != null)
        {
            print("Unloaded preview");
            previewAudioClip.UnloadAudioClip();
            previewAudioClip = null;
        }
    }

    private void UnloadCoverSprite()
    {
        if (coverSprite != null)
        {
            print("Unloaded cover");
            Destroy(coverSprite.texture);
            Destroy(coverSprite);
            coverSprite = null;
        }
    }

    public async void OnStartButton()
    {
        if (Level.IsLocal)
        {
            willStart = true;
            State = ScreenState.Inactive;

            cover.pulseElement.Pulse();
            ProfileWidget.Instance.FadeOut();

            Context.AudioManager.Get("LevelStart").Play();

            if (coverSprite == null)
            {
                await UniTask.WaitUntil(() => coverSprite != null);
            }

            Context.SpriteCache.PutSprite("game://cover", "GameCover", coverSprite);
            coverSprite = null; // Prevent sprite being unloaded by UnloadCoverSprite()

            var sceneLoader = new SceneLoader("Game");
            sceneLoader.Load();

            await UniTask.Delay(TimeSpan.FromSeconds(0.8f));

            cover.mask.DOFade(1, 0.8f);

            await UniTask.Delay(TimeSpan.FromSeconds(0.8f));

            sceneLoader.Activate();
        }
        else
        {
            DownloadAndUnpackLevel();
        }
    }

    public void DownloadAndUnpackLevel()
    {
        if (!Context.OnlinePlayer.IsAuthenticated)
        {
            Toast.Next(Toast.Status.Failure, "Please sign in first.");
            return;
        }
        
        var dialog = Dialog.Instantiate();
        dialog.Message = "Downloading...";
        dialog.UseProgress = true;
        dialog.UsePositiveButton = false;
        dialog.UseNegativeButton = true;

        ulong downloadedSize;
        var totalSize = 0UL;
        var downloading = false;
        var aborted = false;
        var targetFile = $"{Application.temporaryCachePath}/Downloads/{Level.Id}.cytoidlevel";
        var destFolder = $"{Context.DataPath}/{Level.Id}";

        if (Level.IsLocal)
        {
            // Write to the local folder instead
            destFolder = Level.Path;
        }

        // Download detail first, then package
        RequestHelper req;
        var downloadHandler = new DownloadHandlerFile(targetFile)
        {
            removeFileOnAbort = true
        };
        RestClient.Get<OnlineLevel>(req = new RequestHelper
        {
            Uri = $"{Context.ApiBaseUrl}/levels/{Level.Id}"
        }).Then(it =>
        {
            if (aborted)
            {
                throw new OperationCanceledException();
            }

            totalSize = (ulong) it.size;
            downloading = true;
            Debug.Log("Package path: " + Level.PackagePath);
            return RestClient.Get<OnlineLevelResources>(req = new RequestHelper
            {
                Uri = Level.PackagePath,
                Headers = Context.OnlinePlayer.GetJwtAuthorizationHeaders()
            });
        }).Then(res =>
        {
            if (aborted)
            {
                throw new OperationCanceledException();
            }

            Debug.Log("Asset path: " + res.package);
            return RestClient.Get(req = new RequestHelper
            {
                Uri = res.package,
                DownloadHandler = downloadHandler,
                WillParseBody = false
            });
        }).Then(async res =>
        {
            downloading = false;
            dialog.OnNegativeButtonClicked = it => { };
            dialog.UseNegativeButton = false;
            dialog.Progress = 0;
            dialog.Message = "Unpacking...";
            DOTween.To(() => dialog.Progress, value => dialog.Progress = value, 1f, 1f).SetEase(Ease.OutCubic);

            if (Level.IsLocal)
            {
                // Unload the current preview
                if (previewAudioClip != null)
                {
                    previewAudioSource.clip = null;
                    previewAudioClip.UnloadAudioClip();
                }
            }

            var success = await Context.LevelManager.UnpackLevelPackage(targetFile, destFolder);
            if (success)
            {
                // Load with level manager and reload screen
                Level =
                    (await Context.LevelManager.LoadFromMetadataFiles(new List<string> {destFolder + "/level.json"}))
                    .First();
                Context.SelectedLevel = Level;
                Toast.Enqueue(Toast.Status.Success, "Successfully downloaded level.");

                UpdateTopMenu();
                LoadPreview(true);
                LoadCover(true);
                if (!previewAudioSource.isPlaying) LoadPreview(true);
                UpdateStartButton();
            }
            else
            {
                Toast.Next(Toast.Status.Failure, "Could not unpack level.");
            }

            dialog.Close();
            File.Delete(targetFile);
        }).Catch(error =>
        {
            if (aborted || error is OperationCanceledException || (req != null && req.IsAborted))
            {
                Toast.Enqueue(Toast.Status.Success, "Download cancelled.");
            }
            else
            {
                Debug.LogError(error);
                Toast.Next(Toast.Status.Failure, "Could not download level.");
            }

            dialog.Close();
        });

        dialog.onUpdate.AddListener(it =>
        {
            if (!downloading) return;
            if (totalSize > 0)
            {
                downloadedSize = req.DownloadedBytes;
                it.Progress = downloadedSize * 1.0f / totalSize;
                it.Message =
                    $"Downloading... ({downloadedSize.ToHumanReadableFileSize()} / {totalSize.ToHumanReadableFileSize()})";
            }
            else
            {
                it.Message = "Downloading...";
            }
        });
        dialog.OnNegativeButtonClicked = it =>
        {
            aborted = true;
            req?.Abort();
        };
        dialog.Open();
    }

    public void LoadSettings()
    {
        var lp = Context.LocalPlayer;
        practiceModeToggle.Select((!lp.PlayRanked).BoolToString(), false);
        practiceModeToggle.onSelect.AddListener(it =>
        {
            var ranked = !bool.Parse(it);
            lp.PlayRanked = ranked;
            LoadLevelPerformance();
            UpdateStartButton();
        });
        earlyHitSoundsToggle.Select(lp.PlayHitSoundsEarly.BoolToString(), false);
        earlyHitSoundsToggle.onSelect.AddListener(it => lp.PlayHitSoundsEarly = bool.Parse(it));
        largerHitboxesToggle.Select(lp.UseLargerHitboxes.BoolToString(), false);
        largerHitboxesToggle.onSelect.AddListener(it => lp.UseLargerHitboxes = bool.Parse(it));
        earlyLateIndicatorsToggle.Select(lp.DisplayEarlyLateIndicators.BoolToString(), false);
        earlyLateIndicatorsToggle.onSelect.AddListener(it => lp.DisplayEarlyLateIndicators = bool.Parse(it));
        noteSizeCaretSelect.Select(lp.NoteSize.ToString(CultureInfo.InvariantCulture), false, false);
        noteSizeCaretSelect.onSelect.AddListener((_, it) => lp.NoteSize = float.Parse(it));
        horizontalMarginCaretSelect.Select(lp.HorizontalMargin.ToString(CultureInfo.InvariantCulture), false, false);
        horizontalMarginCaretSelect.onSelect.AddListener((_, it) => lp.HorizontalMargin = int.Parse(it));
        verticalMarginCaretSelect.Select(lp.VerticalMargin.ToString(CultureInfo.InvariantCulture), false, false);
        verticalMarginCaretSelect.onSelect.AddListener((_, it) => lp.VerticalMargin = int.Parse(it));
        baseNoteOffsetTextField.SetTextWithoutNotify(lp.BaseNoteOffset.ToString(CultureInfo.InvariantCulture));
        baseNoteOffsetTextField.onEndEdit.AddListener(FloatSettingHandler(baseNoteOffsetTextField,
            () => lp.BaseNoteOffset, value => lp.BaseNoteOffset = value));
        headsetNoteOffsetTextfield.SetTextWithoutNotify(lp.HeadsetNoteOffset.ToString(CultureInfo.InvariantCulture));
        headsetNoteOffsetTextfield.onEndEdit.AddListener(FloatSettingHandler(headsetNoteOffsetTextfield,
            () => lp.HeadsetNoteOffset, value => lp.HeadsetNoteOffset = value));
        storyboardEffectsCaretSelect.Select(lp.GraphicsLevel, false, false);
        storyboardEffectsCaretSelect.onSelect.AddListener((_, it) => lp.GraphicsLevel = it);
        lowerResolutionToggle.Select(lp.LowerResolution.BoolToString(), false);
        lowerResolutionToggle.onSelect.AddListener(it =>
        {
            lp.LowerResolution = bool.Parse(it);
            if (lp.LowerResolution)
            {
                UnityEngine.Screen.SetResolution((int) (Context.InitialWidth * 0.5f), (int) (Context.InitialHeight * 0.5f), true);
            }
            else
            {
                UnityEngine.Screen.SetResolution(Context.InitialWidth, Context.InitialHeight, true);
            }
        });
        showBoundariesToggle.Select(lp.ShowBoundaries.BoolToString(), false);
        showBoundariesToggle.onSelect.AddListener(it => lp.ShowBoundaries = bool.Parse(it));
        coverOpacityCaretSelect.Select(lp.CoverOpacity.ToString(CultureInfo.InvariantCulture), false, false);
        coverOpacityCaretSelect.onSelect.AddListener((_, it) => lp.CoverOpacity = float.Parse(it));
        ringColorTextField.SetTextWithoutNotify(lp.GetRingColor(NoteType.Click, false).ColorToString());
        ringColorTextField.onEndEdit.AddListener(ColorSettingHandler(ringColorTextField,
            () => lp.GetRingColor(NoteType.Click, false), value => lp.SetRingColor(NoteType.Click, false, value)));
        fillColorClickUpTextField.SetTextWithoutNotify(lp.GetFillColor(NoteType.Click, false).ColorToString());
        fillColorClickUpTextField.onEndEdit.AddListener(ColorSettingHandler(fillColorClickUpTextField,
            () => lp.GetFillColor(NoteType.Click, false), value => lp.SetFillColor(NoteType.Click, false, value)));
        fillColorClickDownTextfield.SetTextWithoutNotify(lp.GetFillColor(NoteType.Click, true).ColorToString());
        fillColorClickDownTextfield.onEndEdit.AddListener(ColorSettingHandler(fillColorClickDownTextfield,
            () => lp.GetFillColor(NoteType.Click, true), value => lp.SetFillColor(NoteType.Click, true, value)));
        fillColorDragUpTextField.SetTextWithoutNotify(lp.GetFillColor(NoteType.DragChild, false).ColorToString());
        fillColorDragUpTextField.onEndEdit.AddListener(ColorSettingHandler(fillColorDragUpTextField,
            () => lp.GetFillColor(NoteType.DragChild, false),
            value => lp.SetFillColor(NoteType.DragChild, false, value)));
        fillColorDragDownTextfield.SetTextWithoutNotify(lp.GetFillColor(NoteType.DragChild, true).ColorToString());
        fillColorDragDownTextfield.onEndEdit.AddListener(ColorSettingHandler(fillColorDragDownTextfield,
            () => lp.GetFillColor(NoteType.DragChild, true),
            value => lp.SetFillColor(NoteType.DragChild, true, value)));
        fillColorHoldUpTextField.SetTextWithoutNotify(lp.GetFillColor(NoteType.Hold, false).ColorToString());
        fillColorHoldUpTextField.onEndEdit.AddListener(ColorSettingHandler(fillColorHoldUpTextField,
            () => lp.GetFillColor(NoteType.Hold, false), value => lp.SetFillColor(NoteType.Hold, false, value)));
        fillColorHoldDownTextfield.SetTextWithoutNotify(lp.GetFillColor(NoteType.Hold, true).ColorToString());
        fillColorHoldDownTextfield.onEndEdit.AddListener(ColorSettingHandler(fillColorHoldDownTextfield,
            () => lp.GetFillColor(NoteType.Hold, true), value => lp.SetFillColor(NoteType.Hold, true, value)));
        fillColorLongHoldUpTextField.SetTextWithoutNotify(lp.GetFillColor(NoteType.LongHold, false).ColorToString());
        fillColorLongHoldUpTextField.onEndEdit.AddListener(ColorSettingHandler(fillColorLongHoldUpTextField,
            () => lp.GetFillColor(NoteType.LongHold, false),
            value => lp.SetFillColor(NoteType.LongHold, false, value)));
        fillColorLongHoldDownTextfield.SetTextWithoutNotify(lp.GetFillColor(NoteType.LongHold, true).ColorToString());
        fillColorLongHoldDownTextfield.onEndEdit.AddListener(ColorSettingHandler(fillColorLongHoldDownTextfield,
            () => lp.GetFillColor(NoteType.LongHold, true), value => lp.SetFillColor(NoteType.LongHold, true, value)));
        fillColorFlickUpTextField.SetTextWithoutNotify(lp.GetFillColor(NoteType.Flick, false).ColorToString());
        fillColorFlickUpTextField.onEndEdit.AddListener(ColorSettingHandler(fillColorFlickUpTextField,
            () => lp.GetFillColor(NoteType.Flick, false), value => lp.SetFillColor(NoteType.Flick, false, value)));
        fillColorFlickDownTextfield.SetTextWithoutNotify(lp.GetFillColor(NoteType.Flick, true).ColorToString());
        fillColorFlickDownTextfield.onEndEdit.AddListener(ColorSettingHandler(fillColorFlickDownTextfield,
            () => lp.GetFillColor(NoteType.Flick, true), value => lp.SetFillColor(NoteType.Flick, true, value)));
        displayProfilerToggle.Select(lp.DisplayProfiler.BoolToString(), false);
        displayProfilerToggle.onSelect.AddListener(it => { lp.DisplayProfiler = bool.Parse(it); });
        displayNoteIdsToggle.Select(lp.DisplayNoteIds.BoolToString(), false);
        displayNoteIdsToggle.onSelect.AddListener(it => { lp.DisplayNoteIds = bool.Parse(it); });
    }

    private static UnityAction<string> FloatSettingHandler(InputField inputField, Func<float> defaultValueGetter,
        Action<float> setter)
    {
        return it =>
        {
            if (float.TryParse(it, out var value))
            {
                setter(value);
            }
            else
            {
                inputField.text = defaultValueGetter().ToString(CultureInfo.InvariantCulture);
            }
        };
    }

    private static UnityAction<string> ColorSettingHandler(InputField inputField, Func<Color> defaultValueGetter,
        Action<Color> setter)
    {
        return it =>
        {
            var value = it.ToColor();
            if (value != Color.clear)
            {
                setter(value);
            }
            else
            {
                inputField.text = defaultValueGetter().ColorToString();
            }
        };
    }
}