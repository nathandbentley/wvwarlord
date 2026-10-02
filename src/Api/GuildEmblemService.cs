using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Blish_HUD;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WvWarlord.Models;

namespace WvWarlord.Api
{
    /// <summary>
    /// guild's emblem (background layers tinted + composited behind
    /// foreground layers tinted) and hands it to a caller-supplied setter --
    /// no rotation, no timer, built once per guild and cached.
    /// </summary>
    public class GuildEmblemService : IDisposable
    {
        private const int EmblemSize = 128;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Frees the composited emblem textures this service created (they are its own, not shared).</summary>
        public void Dispose()
        {
            foreach (var tex in _cache.Values) tex?.Dispose();
            _cache.Clear();
        }

        /// <summary>Builds (or returns the cached) emblem texture for this guild. Returns null on failure -- caller should fall back to its original icon.</summary>
        public async Task<Texture2D> GetOrBuildAsync(MyGuildData guild)
        {
            if (guild == null || guild.EmblemBackgroundId <= 0) return null;
            if (_cache.TryGetValue(guild.Id, out var cached)) return cached;

            try
            {
                // 1. Fetch text data endpoints concurrently
                string[] bgUrls = await GetLayerUrlsAsync("backgrounds", guild.EmblemBackgroundId);
                string[] fgUrls = await GetLayerUrlsAsync("foregrounds", guild.EmblemForegroundId);
                Color[] bgColors = await GetColorsRgbAsync(guild.EmblemBackgroundColors);
                Color[] fgColors = await GetColorsRgbAsync(guild.EmblemForegroundColors);

                // 2. Download raw image layer byte arrays from the API endpoints ahead of time
                List<byte[]> bgLayersBytes = await DownloadAllLayerBytesAsync(bgUrls);
                List<byte[]> fgLayersBytes = await DownloadAllLayerBytesAsync(fgUrls);

                // 3. Hand off raw assets to a pure synchronous builder method to bypass the CS4012 ref struct rule
                Texture2D finalTexture = ProcessAndCompositeTextureSynchronously(guild, bgLayersBytes, fgLayersBytes, bgColors, fgColors);

                if (finalTexture != null)
                {
                    _cache[guild.Id] = finalTexture;
                }

                return finalTexture;
            }
            catch (Exception ex)
            {
                ApiCallTracker.RecordCatch();
                ApiCallTracker.Log($"GuildEmblemService build failed for {guild.Id}: {ex.Message}");
                return null;
            }
        }

        // STRICTLY NON-ASYNC: Safe from CS4012 compilation checks because no "await" statement exists inside this method scope
        // REPLACE BOTH METHODS WITH THIS PURE COMPOSITING PATTERN
        private Texture2D ProcessAndCompositeTextureSynchronously(
            MyGuildData guild,
            List<byte[]> bgLayersBytes,
            List<byte[]> fgLayersBytes,
            Color[] bgColors,
            Color[] fgColors)
        {
            // Allocate our safe local CPU image canvas buffer (128x128)
            Color[] compositeBuffer = new Color[EmblemSize * EmblemSize];
            for (int i = 0; i < compositeBuffer.Length; i++) compositeBuffer[i] = Color.Transparent;

            // Process Background Elements, then Foreground Elements sequentially
            CompositeLayersToBuffer(bgLayersBytes, bgColors, guild.EmblemFlags, compositeBuffer, isBackground: true);
            CompositeLayersToBuffer(fgLayersBytes, fgColors, guild.EmblemFlags, compositeBuffer, isBackground: false);

            // Create the final texture asset safely on the main framework thread without deadlocking
            Texture2D final = null;
            using (var deviceContext = GameService.Graphics.LendGraphicsDeviceContext())
            {
                final = new Texture2D(deviceContext.GraphicsDevice, EmblemSize, EmblemSize);
                final.SetData(compositeBuffer);
            }

            return final;
        }

        private void CompositeLayersToBuffer(List<byte[]> layersBytes, Color[] tints, List<string> flags, Color[] destinationBuffer, bool isBackground)
        {
            bool flipH = flags != null && flags.Contains(isBackground ? "FlipBackgroundHorizontal" : "FlipForegroundHorizontal");
            bool flipV = flags != null && flags.Contains(isBackground ? "FlipBackgroundVertical" : "FlipForegroundVertical");

            using (var deviceContext = GameService.Graphics.LendGraphicsDeviceContext())
            {
                var device = deviceContext.GraphicsDevice;

                for (int l = 0; l < layersBytes.Count; l++)
                {
                    if (layersBytes[l] == null || layersBytes[l].Length == 0) continue;

                    Color tint = Color.White;
                    if (tints.Length > 0)
                    {
                        tint = l < tints.Length ? tints[l] : tints[0];
                    }
                    using (var stream = new MemoryStream(layersBytes[l]))
                    {
                        using (var layerTexture = TextureUtil.FromStreamPremultiplied(device, stream))
                        {
                            Color[] layerPixels = new Color[EmblemSize * EmblemSize];
                            layerTexture.GetData(layerPixels);

                            for (int y = 0; y < EmblemSize; y++)
                            {
                                for (int x = 0; x < EmblemSize; x++)
                                {
                                    int srcX = flipH ? (EmblemSize - 1 - x) : x;
                                    int srcY = flipV ? (EmblemSize - 1 - y) : y;

                                    Color rawPixel = layerPixels[srcY * EmblemSize + srcX];

                                    // 1. Extract the raw mask intensity from the layer's Alpha channel
                                    float maskAlpha = rawPixel.A / 255f;
                                    if (maskAlpha == 0) continue; // Skip empty canvas voids instantly

                                    // 2. PROJECT THE TINT: Force the grayscale mask to inherit the exact API color profile
                                    Color srcColor = new Color(
                                        (byte)(tint.R * maskAlpha),
                                        (byte)(tint.G * maskAlpha),
                                        (byte)(tint.B * maskAlpha),
                                        (byte)(tint.A * maskAlpha)
                                    );

                                    int destIndex = y * EmblemSize + x;
                                    Color destColor = destinationBuffer[destIndex];

                                    // 3. Mathematical Alpha Blending pass to overlay the new layer over the previous ones
                                    float srcA = srcColor.A / 255f;
                                    float destA = destColor.A / 255f;
                                    float outA = srcA + destA * (1f - srcA);

                                    if (outA == 0) continue;

                                    destinationBuffer[destIndex].R = (byte)((srcColor.R * srcA + destColor.R * destA * (1f - srcA)) / outA);
                                    destinationBuffer[destIndex].G = (byte)((srcColor.G * srcA + destColor.G * destA * (1f - srcA)) / outA);
                                    destinationBuffer[destIndex].B = (byte)((srcColor.B * srcA + destColor.B * destA * (1f - srcA)) / outA);
                                    destinationBuffer[destIndex].A = (byte)(outA * 255f);
                                }
                            }
                        }
                    }
                }
            }
        }



        private async Task<List<byte[]>> DownloadAllLayerBytesAsync(string[] urls)
        {
            var result = new List<byte[]>();
            foreach (var url in urls)
            {
                try
                {
                    byte[] bytes = await _httpClient.GetByteArrayAsync(url);
                    result.Add(bytes);
                }
                catch (Exception ex)
                {
                    ApiCallTracker.RecordCatch();
                    ApiCallTracker.Log($"GuildEmblemService layer fetch failed for {url}: {ex.Message}");
                    result.Add(Array.Empty<byte>()); // keeps lists aligned
                }
            }
            return result;
        }

        private void DrawLayersSynchronously(SpriteBatch spriteBatch, GraphicsDevice device, List<byte[]> layersBytes, Color[] tints, List<string> flags, bool isBackground)
        {
            var effects = SpriteEffects.None;
            bool flipH = flags != null && flags.Contains(isBackground ? "FlipBackgroundHorizontal" : "FlipForegroundHorizontal");
            bool flipV = flags != null && flags.Contains(isBackground ? "FlipBackgroundVertical" : "FlipForegroundVertical");
            if (flipH) effects |= SpriteEffects.FlipHorizontally;
            if (flipV) effects |= SpriteEffects.FlipVertically;

            for (int i = 0; i < layersBytes.Count; i++)
            {
                if (layersBytes[i] == null || layersBytes[i].Length == 0) continue;
                Color tint = i < tints.Length ? tints[i] : Color.White;

                using (var stream = new MemoryStream(layersBytes[i]))
                {
                    using (var layerTexture = Texture2D.FromStream(device, stream))
                    {
                        spriteBatch.Draw(layerTexture, new Rectangle(0, 0, EmblemSize, EmblemSize), null, tint, 0f, Vector2.Zero, effects, 0f);
                    }
                }
            }
        }

        private async Task<string[]> GetLayerUrlsAsync(string kind, int id)
        {
            if (id <= 0) return Array.Empty<string>();

            string json = await _httpClient.GetStringAsync($"https://api.guildwars2.com/v2/emblem/{kind}?ids={id}");
            using (var doc = JsonDocument.Parse(json))
            {
                var arr = doc.RootElement;
                if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return Array.Empty<string>();

                var entry = arr[0];
                if (!entry.TryGetProperty("layers", out var layersEl)) return Array.Empty<string>();

                return layersEl.EnumerateArray()
                    .Select(l => l.GetString())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }
        }

        private async Task<Color[]> GetColorsRgbAsync(List<int> colorIds)
        {
            if (colorIds == null || colorIds.Count == 0) return Array.Empty<Color>();

            string idsParam = string.Join(",", colorIds);
            string json = await _httpClient.GetStringAsync($"https://api.guildwars2.com/v2/colors?ids={idsParam}");

            var result = new List<Color>();
            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.TryGetProperty("cloth", out var cloth) && cloth.TryGetProperty("rgb", out var rgbArr) && rgbArr.GetArrayLength() == 3)
                    {
                        byte r = (byte)rgbArr[0].GetInt32();
                        byte g = (byte)rgbArr[1].GetInt32();
                        byte b = (byte)rgbArr[2].GetInt32();
                        result.Add(new Color(r: r, g: g, b: b, alpha: (byte)255));
                    }
                    else
                    {
                        result.Add(Color.White);
                    }
                }
            }
            return result.ToArray();
        }
    }
}
