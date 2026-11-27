using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Handlers;
using NativeColor = Android.Graphics.Color;
using NativeView = Android.Views.View;
using Paint = Android.Graphics.Paint;
using Rect = Android.Graphics.Rect;
using RectF = Android.Graphics.RectF;

namespace MusicLayer.Presentation.Maui.Platforms.Android;

internal sealed class LibraryTrackListHandler() : ViewHandler<LibraryTrackList, LibraryRecyclerView>(ListMapper)
{
    private static readonly IPropertyMapper<LibraryTrackList, LibraryTrackListHandler> ListMapper =
        new PropertyMapper<LibraryTrackList, LibraryTrackListHandler>(ViewMapper)
        {
            [nameof(LibraryTrackList.Items)] = (handler, view) => handler.PlatformView.SetItems(view.Items),
            [nameof(LibraryTrackList.ActiveTrack)] = (handler, view) => handler.PlatformView.SetActiveTrack(view.ActiveTrack)
        };

    protected override LibraryRecyclerView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(LibraryRecyclerView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.TrackTapped = track => VirtualView.Tap(track);
    }

    protected override void DisconnectHandler(LibraryRecyclerView platformView)
    {
        platformView.SetItems([]);
        platformView.SetActiveTrack(null);
        platformView.TrackTapped = null;
        base.DisconnectHandler(platformView);
    }
}

internal sealed class LibraryRecyclerView : RecyclerView
{
    private readonly float density;
    private readonly LinearLayoutManager layoutManager;
    private readonly LibraryTrackAdapter adapter;
    private TaskCompletionSource? flingFinished;
    internal Action<LibraryTrack>? TrackTapped { get; set; }

    public LibraryRecyclerView(Context context) : base(context)
    {
        var metrics = context.Resources!.DisplayMetrics!;
        density = metrics.Density;

        layoutManager = new LinearLayoutManager(context, LinearLayoutManager.Vertical, false)
        {
            ItemPrefetchEnabled = true,
            InitialPrefetchItemCount = 10
        };
        adapter = new LibraryTrackAdapter(WaitForFlingAsync, track => TrackTapped?.Invoke(track));

        // Decouple completely from MAUI's outer CoordinatorLayout/AppBarLayout nested scroll interception.
        NestedScrollingEnabled = false;
        HasFixedSize = true;
        OverScrollMode = OverScrollMode.Never;
        VerticalScrollBarEnabled = true;
        HorizontalScrollBarEnabled = false;
        SetClipToPadding(false);
        SetPadding(0, 0, 0, (int)Math.Ceiling(12f * density));
        SetItemAnimator(null);
        SetItemViewCacheSize(16);
        GetRecycledViewPool().SetMaxRecycledViews(0, 24);
        SetLayoutManager(layoutManager);
        AddItemDecoration(new RowSpacingDecoration((int)Math.Round(8f * density)));
        AddOnScrollListener(new FlingPauseListener(this));
        SetAdapter(adapter);
    }

    public void SetItems(IReadOnlyList<LibraryTrack>? items)
    {
        StopScroll();
        adapter.Update(items ?? []);
        ScrollToPosition(0);
        SetFlinging(false);
    }

    public void SetActiveTrack(LibraryTrack? track) => adapter.SetActiveTrack(track);

    protected override void OnDetachedFromWindow()
    {
        SetFlinging(false);
        base.OnDetachedFromWindow();
    }

    private Task WaitForFlingAsync() => flingFinished?.Task ?? Task.CompletedTask;

    private void SetFlinging(bool flinging)
    {
        if (flinging)
        {
            flingFinished ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        else
        {
            var pending = flingFinished;
            flingFinished = null;
            pending?.TrySetResult();
        }
    }

    protected override void OnMeasure(int widthSpec, int heightSpec)
    {
        // MAUI passes AtMost for star-sized Grid cells; converting to Exactly hits RecyclerView's fast-path
        // and avoids running a redundant full child layout pass inside OnMeasure.
        if (MeasureSpec.GetMode(widthSpec) == MeasureSpecMode.AtMost)
            widthSpec = MeasureSpec.MakeMeasureSpec(MeasureSpec.GetSize(widthSpec), MeasureSpecMode.Exactly);
        if (MeasureSpec.GetMode(heightSpec) == MeasureSpecMode.AtMost)
            heightSpec = MeasureSpec.MakeMeasureSpec(MeasureSpec.GetSize(heightSpec), MeasureSpecMode.Exactly);
        base.OnMeasure(widthSpec, heightSpec);
    }

    private sealed class RowSpacingDecoration(int spacePx) : ItemDecoration
    {
        public override void GetItemOffsets(Rect outRect, NativeView view, RecyclerView parent, State state)
        {
            outRect.Set(0, 0, 0, spacePx);
        }
    }

    private sealed class FlingPauseListener(LibraryRecyclerView owner) : OnScrollListener
    {
        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState) =>
            owner.SetFlinging(newState == ScrollStateSettling);
    }
}

internal sealed class LibraryTrackAdapter(Func<Task> waitForFling, Action<LibraryTrack> tapTrack) : RecyclerView.Adapter
{
    private IReadOnlyList<LibraryTrack> items = [];
    private LibraryTrack? activeTrack;

    public override int ItemCount => items.Count;

    public void Update(IReadOnlyList<LibraryTrack> updated)
    {
        items = updated;
        NotifyDataSetChanged();
    }

    public void SetActiveTrack(LibraryTrack? current)
    {
        if (ReferenceEquals(activeTrack, current)) return;
        var previous = activeTrack;
        activeTrack = current;
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], previous) || ReferenceEquals(items[i], current))
                NotifyItemChanged(i);
        }
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType) =>
        new RowHolder(new LibrarySongRowView(parent.Context!, waitForFling, tapTrack));

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is RowHolder rowHolder)
            rowHolder.Row.Bind(items[position], position + 1, ReferenceEquals(items[position], activeTrack));
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is RowHolder rowHolder)
            rowHolder.Row.Unbind();
        base.OnViewRecycled(holder);
    }

    private sealed class RowHolder(LibrarySongRowView row) : RecyclerView.ViewHolder(row)
    {
        public LibrarySongRowView Row { get; } = row;
    }
}

internal sealed class LibrarySongRowView : NativeView
{
    private static readonly SemaphoreSlim DecodeGate = new(2, 2);

    private readonly float density;
    private readonly int rowHeightPx;
    private readonly Paint surface = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(11, 29, 34) };
    private readonly Paint activeSurface = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(16, 56, 50) };
    private readonly Paint border = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(32, 72, 63), StrokeWidth = 1f };
    private readonly Paint coverBackground = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(18, 51, 47) };
    private readonly Paint bitmapPaint = new(PaintFlags.FilterBitmap);
    private readonly TextPaint titlePaint = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(242, 248, 247) };
    private readonly TextPaint creditsPaint = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(154, 185, 181) };
    private readonly TextPaint durationPaint = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(154, 185, 181), TextAlign = Paint.Align.Right };
    private readonly TextPaint numberPaint = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(122, 158, 154), TextAlign = Paint.Align.Center };
    private readonly Drawable? fallback;
    private readonly Func<Task> waitForFling;
    private readonly RectF cardRect = new();
    private readonly RectF destination = new();
    private LibraryTrack? track;
    private Bitmap? cover;
    private string title = "";
    private string credits = "";
    private string number = "";
    private bool active;
    private int version;

    public LibrarySongRowView(Context context, Func<Task> waitForFling, Action<LibraryTrack> tapTrack) : base(context)
    {
        this.waitForFling = waitForFling;
        Click += (_, _) => { if (track is { } item) tapTrack(item); };
        var metrics = context.Resources!.DisplayMetrics!;
        density = metrics.Density;
        border.SetStyle(Paint.Style.Stroke);
        border.StrokeWidth = Math.Max(1f, density * 0.85f);
        titlePaint.SetTypeface(Typeface.DefaultBold);
        titlePaint.TextSize = TypedValue.ApplyDimension(ComplexUnitType.Sp, 15, metrics);
        creditsPaint.TextSize = TypedValue.ApplyDimension(ComplexUnitType.Sp, 13, metrics);
        durationPaint.TextSize = TypedValue.ApplyDimension(ComplexUnitType.Sp, 11, metrics);
        numberPaint.TextSize = durationPaint.TextSize;
        rowHeightPx = (int)Math.Ceiling(Math.Max(Dp(64), titlePaint.TextSize + creditsPaint.TextSize + Dp(20)));
        fallback = context.GetDrawable(Resource.Drawable.sixteenth_notes)?.Mutate();
        LayoutParameters = new RecyclerView.LayoutParams(ViewGroup.LayoutParams.MatchParent, rowHeightPx);
        ImportantForAccessibility = ImportantForAccessibility.Yes;
    }

    private float Dp(float value) => value * density;

    public void Bind(LibraryTrack value, int position, bool isActive)
    {
        int request = ++version;
        track = value;
        active = isActive;
        number = isActive ? "●" : position.ToString();
        titlePaint.Color = isActive ? NativeColor.Rgb(37, 232, 164) : NativeColor.Rgb(242, 248, 247);
        border.Color = isActive ? NativeColor.Rgb(47, 116, 99) : NativeColor.Rgb(32, 72, 63);
        numberPaint.Color = isActive ? NativeColor.Rgb(37, 232, 164) : NativeColor.Rgb(122, 158, 154);
        UpdateText(value);

        string? path = value.ArtworkPath;
        cover = path is null ? null : LibraryCoverCache.Find(path);
        if (path is not null && cover is null)
            _ = LoadCoverAsync(path, request);

        Invalidate();
    }

    public void Unbind()
    {
        ++version;
        track = null;
        cover = null;
        title = "";
        credits = "";
    }

    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is not null && track is not null)
            info.ContentDescription = active ? $"{track.AccessibilityLabel}. Pista activa." : track.AccessibilityLabel;
    }

    private async Task LoadCoverAsync(string path, int request)
    {
        await waitForFling().ConfigureAwait(false);
        if (request != Volatile.Read(ref version)) return;

        Bitmap? bitmap = await Task.Run(async () =>
        {
            if (request != Volatile.Read(ref version)) return null;
            await DecodeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return request == Volatile.Read(ref version) ? LibraryCoverCache.Load(path) : null;
            }
            finally
            {
                DecodeGate.Release();
            }
        }).ConfigureAwait(false);

        if (bitmap is not null && request == Volatile.Read(ref version))
        {
            Post(() =>
            {
                if (request != version) return;
                cover = bitmap;
                Invalidate();
            });
        }
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        SetMeasuredDimension(MeasureSpec.GetSize(widthMeasureSpec), rowHeightPx);
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        float halfStroke = border.StrokeWidth * 0.5f;
        cardRect.Set(halfStroke, halfStroke, w - halfStroke, h - halfStroke);
        float coverTop = (h - Dp(44)) * 0.5f;
        destination.Set(Dp(34), coverTop, Dp(78), coverTop + Dp(44));
        int iconTop = (int)((h - Dp(22)) * 0.5f);
        fallback?.SetBounds((int)Dp(45), iconTop, (int)Dp(67), iconTop + (int)Dp(22));
        if (track is not null) UpdateText(track);
    }

    private void UpdateText(LibraryTrack current)
    {
        if (Width <= 0)
        {
            title = current.Title;
            credits = current.Credits;
            return;
        }

        if (current.CachedDurationWidth < 0f)
            current.CachedDurationWidth = durationPaint.MeasureText(current.Duration);

        int availablePx = (int)(Width - Dp(105) - current.CachedDurationWidth);
        if (availablePx <= 0)
        {
            title = "";
            credits = "";
            return;
        }

        if (current.CachedTextWidth != availablePx)
        {
            current.CachedTitle = TextUtils.Ellipsize(current.Title, titlePaint, availablePx, TextUtils.TruncateAt.End!) ?? "";
            current.CachedCredits = TextUtils.Ellipsize(current.Credits, creditsPaint, availablePx, TextUtils.TruncateAt.End!) ?? "";
            current.CachedTextWidth = availablePx;
        }

        title = current.CachedTitle;
        credits = current.CachedCredits;
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (track is null) return;

        float corner = Dp(10);
        canvas.DrawRoundRect(cardRect, corner, corner, active ? activeSurface : surface);
        canvas.DrawRoundRect(cardRect, corner, corner, border);

        if (cover is { } bitmap)
        {
            canvas.DrawBitmap(bitmap, null, destination, bitmapPaint);
        }
        else
        {
            canvas.DrawRoundRect(destination, Dp(6), Dp(6), coverBackground);
            fallback?.Draw(canvas);
        }

        float middle = Height * 0.5f;
        canvas.DrawText(number, Dp(17), middle - (numberPaint.Ascent() + numberPaint.Descent()) * 0.5f, numberPaint);
        canvas.DrawText(title, Dp(88), middle - Dp(3), titlePaint);
        canvas.DrawText(credits, Dp(88), middle + creditsPaint.TextSize, creditsPaint);
        canvas.DrawText(track.Duration, Width - Dp(10), middle - (durationPaint.Ascent() + durationPaint.Descent()) * 0.5f, durationPaint);
    }
}
