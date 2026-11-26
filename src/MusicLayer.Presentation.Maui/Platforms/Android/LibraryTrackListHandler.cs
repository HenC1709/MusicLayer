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
            [nameof(LibraryTrackList.Items)] = (handler, view) => handler.PlatformView.SetItems(view.Items)
        };

    protected override LibraryRecyclerView CreatePlatformView() => new(Context);

    protected override void DisconnectHandler(LibraryRecyclerView platformView)
    {
        platformView.SetItems([]);
        base.DisconnectHandler(platformView);
    }
}

internal sealed class LibraryRecyclerView : RecyclerView
{
    private readonly float density;
    private readonly int maxFlingVelocity;
    private readonly LinearLayoutManager layoutManager;
    private readonly LibraryTrackAdapter adapter;
    private readonly FastScrollOverlay fastScroll;
    private bool draggingThumb;

    public LibraryRecyclerView(Context context) : base(context)
    {
        var metrics = context.Resources!.DisplayMetrics!;
        density = metrics.Density;
        maxFlingVelocity = ViewConfiguration.Get(context)?.ScaledMaximumFlingVelocity ?? (int)(8000f * density);

        layoutManager = new LinearLayoutManager(context, LinearLayoutManager.Vertical, false)
        {
            ItemPrefetchEnabled = true,
            InitialPrefetchItemCount = 10
        };
        adapter = new LibraryTrackAdapter();
        fastScroll = new FastScrollOverlay(this, density);

        // Decouple completely from MAUI's outer CoordinatorLayout/AppBarLayout nested scroll interception.
        NestedScrollingEnabled = false;
        HasFixedSize = true;
        OverScrollMode = OverScrollMode.Always;
        VerticalScrollBarEnabled = false;
        HorizontalScrollBarEnabled = false;
        SetClipToPadding(false);
        SetPadding(0, 0, 0, (int)Math.Ceiling(12f * density));
        SetItemAnimator(null);
        SetItemViewCacheSize(16);
        GetRecycledViewPool().SetMaxRecycledViews(0, 24);
        SetLayoutManager(layoutManager);
        AddItemDecoration(new RowSpacingDecoration((int)Math.Round(8f * density)));
        AddItemDecoration(fastScroll);
        AddOnScrollListener(new FastScrollListener(fastScroll));
        SetAdapter(adapter);
    }

    public void SetItems(IReadOnlyList<LibraryTrack>? items)
    {
        StopScroll();
        adapter.Update(items ?? []);
        ScrollToPosition(0);
        fastScroll.OnItemsChanged();
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

    public override bool OnInterceptTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;

        if (e.ActionMasked == MotionEventActions.Down)
        {
            Parent?.RequestDisallowInterceptTouchEvent(true);
            if (fastScroll.TryBeginDrag(e.GetX(), e.GetY()))
            {
                StopScroll();
                draggingThumb = true;
                return true;
            }
            draggingThumb = false;
        }

        return base.OnInterceptTouchEvent(e);
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null) return false;

        if (draggingThumb)
        {
            switch (e.ActionMasked)
            {
                case MotionEventActions.Move:
                    fastScroll.DragTo(e.GetY(), layoutManager, adapter.ItemCount);
                    return true;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    draggingThumb = false;
                    fastScroll.EndDrag();
                    return true;
            }
        }

        return base.OnTouchEvent(e);
    }

    public override bool Fling(int velocityX, int velocityY)
    {
        float vyDp = Math.Abs(velocityY) / density;
        if (vyDp > 900f)
        {
            // Smoothly ramp moderate single-finger flicks so comfortable swipes glide across long lists
            // without requiring aggressive thumb flicks, while keeping slow flicks completely natural.
            float boost = 1f + Math.Min(0.85f, (vyDp - 900f) / 2600f);
            long scaled = (long)(velocityY * boost);
            velocityY = (int)Math.Clamp(scaled, -maxFlingVelocity, maxFlingVelocity);
        }

        return base.Fling(velocityX, velocityY);
    }

    private sealed class RowSpacingDecoration(int spacePx) : ItemDecoration
    {
        public override void GetItemOffsets(Rect outRect, NativeView view, RecyclerView parent, State state)
        {
            outRect.Set(0, 0, 0, spacePx);
        }
    }

    private sealed class FastScrollListener(FastScrollOverlay overlay) : OnScrollListener
    {
        public override void OnScrolled(RecyclerView recyclerView, int dx, int dy)
        {
            if (dy != 0) overlay.NotifyScrolled();
        }
    }

    private sealed class FastScrollOverlay : ItemDecoration
    {
        private const long HoldDurationMs = 1200;
        private const long FadeDurationMs = 240;

        private readonly RecyclerView owner;
        private readonly float density;
        private readonly Paint trackPaint = new(PaintFlags.AntiAlias) { Color = NativeColor.Argb(45, 32, 72, 63) };
        private readonly Paint thumbPaint = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(37, 232, 164) };
        private readonly RectF rect = new();
        private long lastActiveUptime;
        private float thumbTop;
        private float thumbBottom;
        private bool activeDrag;
        private bool visible;

        public FastScrollOverlay(RecyclerView owner, float density)
        {
            this.owner = owner;
            this.density = density;
        }

        private float Dp(float value) => value * density;

        public void OnItemsChanged()
        {
            activeDrag = false;
            visible = false;
            owner.Invalidate();
        }

        public void NotifyScrolled()
        {
            lastActiveUptime = SystemClock.UptimeMillis();
            visible = true;
            InvalidateStrip();
        }

        public bool TryBeginDrag(float x, float y)
        {
            if (!visible || owner.Width <= 0) return false;
            if (x < owner.Width - Dp(30)) return false;
            if (y < thumbTop - Dp(14) || y > thumbBottom + Dp(14)) return false;

            activeDrag = true;
            lastActiveUptime = SystemClock.UptimeMillis();
            InvalidateStrip();
            return true;
        }

        public void DragTo(float y, LinearLayoutManager manager, int itemCount)
        {
            if (itemCount <= 0) return;

            float minThumbHeight = Dp(40);
            float padVertical = Dp(4);
            float trackHeight = Math.Max(1f, owner.Height - 2f * padVertical);
            float thumbHeight = Math.Max(minThumbHeight, thumbBottom - thumbTop);
            float scrollableTrack = Math.Max(1f, trackHeight - thumbHeight);
            float fraction = Math.Clamp((y - padVertical - thumbHeight * 0.5f) / scrollableTrack, 0f, 1f);

            int targetIndex = Math.Clamp((int)Math.Round(fraction * (itemCount - 1)), 0, itemCount - 1);
            manager.ScrollToPositionWithOffset(targetIndex, 0);
            lastActiveUptime = SystemClock.UptimeMillis();
            visible = true;
            InvalidateStrip();
        }

        public void EndDrag()
        {
            activeDrag = false;
            lastActiveUptime = SystemClock.UptimeMillis();
            InvalidateStrip();
        }

        private void InvalidateStrip()
        {
            int left = Math.Max(0, owner.Width - (int)Math.Ceiling(Dp(14)));
            owner.PostInvalidateOnAnimation(left, 0, owner.Width, owner.Height);
        }

        public override void OnDrawOver(Canvas c, RecyclerView parent, State state)
        {
            if (!visible) return;

            int range = parent.ComputeVerticalScrollRange();
            int extent = parent.ComputeVerticalScrollExtent();
            if (extent <= 0 || range <= extent * 1.35f)
            {
                visible = false;
                return;
            }

            int alpha = 255;
            if (!activeDrag)
            {
                long elapsed = SystemClock.UptimeMillis() - lastActiveUptime;
                if (elapsed >= HoldDurationMs + FadeDurationMs)
                {
                    visible = false;
                    return;
                }
                if (elapsed > HoldDurationMs)
                {
                    float t = 1f - (float)(elapsed - HoldDurationMs) / FadeDurationMs;
                    alpha = Math.Clamp((int)(255f * t), 0, 255);
                }
                InvalidateStrip();
            }

            float padVertical = Dp(4);
            float trackHeight = parent.Height - 2f * padVertical;
            if (trackHeight <= Dp(40)) return;

            int offset = parent.ComputeVerticalScrollOffset();
            float thumbHeight = Math.Clamp(trackHeight * ((float)extent / range), Dp(40), trackHeight);
            float fraction = Math.Clamp((float)offset / Math.Max(1, range - extent), 0f, 1f);
            thumbTop = padVertical + fraction * (trackHeight - thumbHeight);
            thumbBottom = thumbTop + thumbHeight;

            float width = activeDrag ? Dp(6) : Dp(3.5f);
            float right = parent.Width - Dp(1.5f);
            float left = right - width;

            if (activeDrag)
            {
                trackPaint.Alpha = 70;
                rect.Set(left, padVertical, right, parent.Height - padVertical);
                c.DrawRoundRect(rect, width * 0.5f, width * 0.5f, trackPaint);
            }

            thumbPaint.Alpha = activeDrag ? 255 : (int)(alpha * 0.85f);
            rect.Set(left, thumbTop, right, thumbBottom);
            c.DrawRoundRect(rect, width * 0.5f, width * 0.5f, thumbPaint);
        }
    }
}

internal sealed class LibraryTrackAdapter : RecyclerView.Adapter
{
    private IReadOnlyList<LibraryTrack> items = [];

    public override int ItemCount => items.Count;

    public void Update(IReadOnlyList<LibraryTrack> updated)
    {
        items = updated;
        NotifyDataSetChanged();
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType) =>
        new RowHolder(new LibrarySongRowView(parent.Context!));

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is RowHolder rowHolder)
            rowHolder.Row.Bind(items[position]);
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
    private readonly Paint border = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(32, 72, 63), StrokeWidth = 1f };
    private readonly Paint coverBackground = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(18, 51, 47) };
    private readonly Paint bitmapPaint = new(PaintFlags.FilterBitmap);
    private readonly TextPaint titlePaint = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(242, 248, 247) };
    private readonly TextPaint creditsPaint = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(154, 185, 181) };
    private readonly TextPaint formatPaint = new(PaintFlags.AntiAlias) { Color = NativeColor.Rgb(37, 232, 164), TextAlign = Paint.Align.Right };
    private readonly Drawable? fallback;
    private readonly RectF cardRect = new();
    private readonly RectF destination = new();
    private LibraryTrack? track;
    private Bitmap? cover;
    private string title = "";
    private string credits = "";
    private int version;

    public LibrarySongRowView(Context context) : base(context)
    {
        var metrics = context.Resources!.DisplayMetrics!;
        density = metrics.Density;
        border.SetStyle(Paint.Style.Stroke);
        border.StrokeWidth = Math.Max(1f, density * 0.85f);
        titlePaint.SetTypeface(Typeface.DefaultBold);
        titlePaint.TextSize = TypedValue.ApplyDimension(ComplexUnitType.Sp, 15, metrics);
        creditsPaint.TextSize = TypedValue.ApplyDimension(ComplexUnitType.Sp, 13, metrics);
        formatPaint.TextSize = TypedValue.ApplyDimension(ComplexUnitType.Sp, 11, metrics);
        rowHeightPx = (int)Math.Ceiling(Math.Max(Dp(64), titlePaint.TextSize + creditsPaint.TextSize + Dp(20)));
        fallback = context.GetDrawable(Resource.Drawable.sixteenth_notes)?.Mutate();
        LayoutParameters = new RecyclerView.LayoutParams(ViewGroup.LayoutParams.MatchParent, rowHeightPx);
        ImportantForAccessibility = ImportantForAccessibility.Yes;
    }

    private float Dp(float value) => value * density;

    public void Bind(LibraryTrack value)
    {
        int request = ++version;
        track = value;
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
            info.ContentDescription = track.AccessibilityLabel;
    }

    private async Task LoadCoverAsync(string path, int request)
    {
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
        destination.Set(Dp(9), coverTop, Dp(53), coverTop + Dp(44));
        int iconTop = (int)((h - Dp(22)) * 0.5f);
        fallback?.SetBounds((int)Dp(20), iconTop, (int)Dp(42), iconTop + (int)Dp(22));
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

        if (current.CachedFormatWidth < 0f)
            current.CachedFormatWidth = formatPaint.MeasureText(current.Format);

        int availablePx = (int)(Width - Dp(82) - current.CachedFormatWidth);
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
        canvas.DrawRoundRect(cardRect, corner, corner, surface);
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
        canvas.DrawText(title, Dp(63), middle - Dp(3), titlePaint);
        canvas.DrawText(credits, Dp(63), middle + creditsPaint.TextSize, creditsPaint);
        canvas.DrawText(track.Format, Width - Dp(10), middle - (formatPaint.Ascent() + formatPaint.Descent()) * 0.5f, formatPaint);
    }
}
