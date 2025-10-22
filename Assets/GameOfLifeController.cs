using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(SpriteRenderer))]
public class GameOfLifeController : MonoBehaviour
{
    public int width = 80;
    public int height = 50;
    [Range(1, 32)] public int pixelsPerCell = 8;
    public Color bgColor = new Color(0.05f, 0.05f, 0.05f);
    public Color p1Color = Color.white;
    public Color p2Color = new Color(1f, 0.85f, 0.2f);
    public bool autoRun = false;
    [Range(0.02f, 1f)] public float updateInterval = 0.15f;
    public enum Phase { SetupP1, SetupP2, Running, Paused, Finished }
    public int seedsPerPlayer = 20;
    public Phase phase = Phase.SetupP1;
    public Text p1Hud;
    public Text p2Hud;
    public Text phaseHud;
    private byte[,] cur;
    private byte[,] nxt;
    private Texture2D tex;
    private SpriteRenderer sr;
    private float t;
    private int p1Left, p2Left;
    private int p1Score, p2Score;
    public Button playButton;
    public Button stepButton;
    public Button resetButton;
    public Slider speedSlider;
    [SerializeField] float zoomSpeed = 5f;
    [SerializeField] float minOrtho = 8f;
    [SerializeField] float maxOrtho = 60f;
    [SerializeField] float animDuration = 0.12f;
    float[,] animT;
    byte[,] prevState;
    bool animating;
    int p1Total, p2Total;
    public GameObject resultPanel;
    public TMP_Text resultText;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        InitGrids();
        CreateTexture();
        ClearAll();
        p1Left = seedsPerPlayer;
        p2Left = seedsPerPlayer;
        FitCameraToTexture();
        RedrawTextureFull();
        RefreshHUD();
    }

    void Update()
    {
        HandleZoom();
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (phase == Phase.Running) { phase = Phase.Paused; autoRun = false; }
            else if (phase == Phase.Paused) { phase = Phase.Running; autoRun = true; }
            RefreshHUD();
        }
        if (Input.GetKeyDown(KeyCode.C)) { ClearAll(); RedrawTextureFull(); }
        if (Input.GetKeyDown(KeyCode.S))
        {
            if (phase == Phase.Running || phase == Phase.Paused)
            {
                Step();
                RedrawTextureFull();
                CheckForEnd();
            }
        }
        if (phase == Phase.SetupP1 || phase == Phase.SetupP2)
            HandleSetupInput();
        if (phase == Phase.Running && autoRun)
        {
            t += Time.deltaTime;
            if (t >= updateInterval)
            {
                Step();
                RedrawTextureFull();
                t = 0f;
                CheckForEnd();
            }
        }
        if (animating)
        {
            bool any = false;
            float dt = Time.deltaTime;
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (animT[x, y] > 0f)
                {
                    animT[x, y] = Mathf.Max(0f, animT[x, y] - dt);
                    if (animT[x, y] > 0f) any = true;
                }
            }
            animating = any;
            RedrawTextureFull();
        }
    }

    void HandleZoom()
    {
        var cam = Camera.main;
        if (!cam) return;
        float delta = -Input.mouseScrollDelta.y;
        if (Mathf.Abs(delta) > 0.0001f)
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize + delta * zoomSpeed, minOrtho, maxOrtho);
    }

    void HandleSetupInput()
    {
        int curPlayer = (phase == Phase.SetupP1) ? 1 : 2;
        int left = (curPlayer == 1) ? p1Left : p2Left;
        if (Input.GetMouseButtonDown(0))
        {
            if (left > 0 && WorldToCell(Camera.main.ScreenToWorldPoint(Input.mousePosition), out int cx, out int cy))
            {
                if (cur[cx, cy] == 0)
                {
                    cur[cx, cy] = (byte)curPlayer;
                    PaintCellOnTexture(cx, cy, curPlayer == 1 ? p1Color : p2Color);
                    tex.Apply(false);
                    if (curPlayer == 1) p1Left--; else p2Left--;
                    NextIfNoLeft();
                    RefreshHUD();
                }
            }
        }
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.E))
        {
            if (WorldToCell(Camera.main.ScreenToWorldPoint(Input.mousePosition), out int cx, out int cy))
            {
                if (cur[cx, cy] == curPlayer)
                {
                    cur[cx, cy] = 0;
                    PaintCellOnTexture(cx, cy, bgColor);
                    tex.Apply(false);
                    if (curPlayer == 1) p1Left++; else p2Left++;
                    RefreshHUD();
                }
            }
        }
        if (Input.GetKeyDown(KeyCode.Return)) NextPhaseManually();
    }

    void NextIfNoLeft()
    {
        if (phase == Phase.SetupP1 && p1Left == 0) { phase = Phase.SetupP2; }
        else if (phase == Phase.SetupP2 && p2Left == 0) { phase = Phase.Running; autoRun = true; }
        RefreshHUD();
    }

    void NextPhaseManually()
    {
        if (phase == Phase.SetupP1) phase = Phase.SetupP2;
        else if (phase == Phase.SetupP2) { phase = Phase.Running; autoRun = true; }
        RefreshHUD();
    }

    void Step()
    {
        p1Score = 0;
        p2Score = 0;
        int aliveTotal = 0;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            byte state = cur[x, y];
            bool alive = state != 0;
            int n1 = 0, n2 = 0, nTot = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                byte s = cur[nx, ny];
                if (s == 1) { n1++; nTot++; }
                else if (s == 2) { n2++; nTot++; }
            }
            byte ns = 0;
            if (alive)
            {
                bool survive = (nTot == 2 || nTot == 3);
                ns = survive ? state : (byte)0;
                if (survive) aliveTotal++;
            }
            else
            {
                if (nTot == 3)
                {
                    if (n1 > n2) ns = 1;
                    else if (n2 > n1) ns = 2;
                    else ns = 0;
                    if (ns == 1) { p1Score++; aliveTotal++; }
                    else if (ns == 2) { p2Score++; aliveTotal++; }
                }
            }
            byte old = state;
            nxt[x, y] = ns;
            if (ns != old)
            {
                prevState[x, y] = old;
                animT[x, y] = animDuration;
                animating = true;
            }
        }
        (cur, nxt) = (nxt, cur);
    }

    void CheckForEnd()
    {
        if (CountAlive(cur) == 0)
        {
            phase = Phase.Finished;
            autoRun = false;
            string winner;
            if (p1Total > p2Total) winner = $"P1 wins!  {p1Total} : {p2Total}";
            else if (p2Total > p1Total) winner = $"P2 wins!  {p2Total} : {p1Total}";
            else winner = $"Draw!  {p1Total} : {p2Total}";
            ShowResult(winner + "\nPress OK or Reset");
            RefreshHUD();
            return;
        }
        RefreshHUD();
    }

    int CountAlive(byte[,] grid)
    {
        int a = 0;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            if (grid[x, y] != 0) a++;
        return a;
    }

    void AccumulateScores()
    {
        p1Total += p1Score;
        p2Total += p2Score;
    }

    int SumScore(int player) => (player == 1) ? p1Total : p2Total;

    void InitGrids()
    {
        cur = new byte[width, height];
        nxt = new byte[width, height];
        animT = new float[width, height];
        prevState = new byte[width, height];
    }

    void CreateTexture()
    {
        int w = width * pixelsPerCell;
        int h = height * pixelsPerCell;
        tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), pixelsPerCell);
        sr.sprite = sprite;
    }

    Color StateToColor(byte s)
    {
        if (s == 1) return p1Color;
        if (s == 2) return p2Color;
        return bgColor;
    }

    void RedrawTextureFull()
    {
        int p = pixelsPerCell;
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            byte s = cur[x, y];
            Color col;
            if (animT[x, y] > 0f)
            {
                float t = 1f - (animT[x, y] / animDuration);
                Color from = StateToColor(prevState[x, y]);
                Color to = StateToColor(s);
                col = Color.Lerp(from, to, t);
            }
            else col = StateToColor(s);
            for (int ix = 0; ix < p; ix++)
            for (int iy = 0; iy < p; iy++)
                tex.SetPixel(x * p + ix, y * p + iy, col);
        }
        tex.Apply(false);
        AccumulateScores();
    }

    void PaintCellOnTexture(int x, int y, Color col)
    {
        int p = pixelsPerCell;
        for (int ix = 0; ix < p; ix++)
        for (int iy = 0; iy < p; iy++)
            tex.SetPixel(x * p + ix, y * p + iy, col);
    }

    void ClearAll()
    {
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            cur[x, y] = 0;
        p1Total = p2Total = 0;
        p1Score = p2Score = 0;
        p1Left = p2Left = seedsPerPlayer;
        System.Array.Clear(animT, 0, animT.Length);
        System.Array.Clear(prevState, 0, prevState.Length);
        animating = false;
        phase = Phase.SetupP1;
        autoRun = false;
        RefreshHUD();
    }

    void FitCameraToTexture()
    {
        var cam = Camera.main;
        if (cam == null) return;
        float worldW = width;
        float worldH = height;
        transform.position = Vector3.zero;
        sr.transform.position = Vector3.zero;
        cam.transform.position = new Vector3(0, 0, -10);
        float aspect = cam.aspect;
        float neededSize = Mathf.Max(worldH * 0.5f, worldW * 0.5f / aspect);
        cam.orthographic = true;
        cam.orthographicSize = neededSize + 0.5f;
        cam.backgroundColor = Color.black;
    }

    bool WorldToCell(Vector3 world, out int cx, out int cy)
    {
        float left = -width * 0.5f;
        float bottom = -height * 0.5f;
        float fx = world.x - left;
        float fy = world.y - bottom;
        cx = Mathf.FloorToInt(fx);
        cy = Mathf.FloorToInt(fy);
        return (cx >= 0 && cx < width && cy >= 0 && cy < height);
    }

    public void UiToggleRun()
    {
        if (phase == Phase.Running) { phase = Phase.Paused; autoRun = false; }
        else if (phase == Phase.Paused) { phase = Phase.Running; autoRun = true; }
        RefreshHUD();
    }

    public void UiStep()
    {
        if (phase == Phase.Running || phase == Phase.Paused)
        {
            Step();
            RedrawTextureFull();
            CheckForEnd();
        }
    }

    public void UiReset()
    {
        ClearAll();
        RedrawTextureFull();
    }

    public void UiSetSpeed(float t)
    {
        updateInterval = Mathf.Lerp(0.5f, 0.05f, t);
        Debug.Log($"Speed = {updateInterval:F3}");
    }

    void RefreshHUD()
    {
        if (phaseHud) phaseHud.text = $"Phase: {phase}";
        if (p1Hud) p1Hud.text = $"P1  score: {p1Total}   left: {p1Left}";
        if (p2Hud) p2Hud.text = $"P2  score: {p2Total}   left: {p2Left}";
    }

    void ShowResult(string msg)
    {
        if (resultText) resultText.text = msg;
        if (resultPanel) resultPanel.SetActive(true);
    }

    public void UiCloseResult()
    {
        if (resultPanel) resultPanel.SetActive(false);
    }
}
