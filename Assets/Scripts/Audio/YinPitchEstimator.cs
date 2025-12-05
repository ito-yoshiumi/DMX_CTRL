using UnityEngine;
using System.Collections.Generic;

namespace Encounter.Audio
{
    /// <summary>
    /// YINアルゴリズムによるピッチ推定クラス
    /// 参照: "YIN, a fundamental frequency estimator for speech and music" (De Cheveigné & Kawahara, 2002)
    /// </summary>
    public class YinPitchEstimator
    {
        private float _sampleRate;
        private int _bufferSize;
        private float _threshold; // 閾値（通常0.1〜0.15）
        
        // 内部バッファ
        private float[] _yinBuffer;
        
        public YinPitchEstimator(float sampleRate, int bufferSize, float threshold = 0.15f)
        {
            _sampleRate = sampleRate;
            _bufferSize = bufferSize;
            _threshold = threshold;
            
            // YINバッファはバッファサイズの半分で十分（tauの最大値）
            _yinBuffer = new float[_bufferSize / 2];
        }

        /// <summary>
        /// オーディオバッファからピッチを推定する
        /// </summary>
        /// <param name="audioBuffer">入力オーディオバッファ</param>
        /// <returns>推定されたピッチ(Hz)。検出できない場合は-1</returns>
        public float GetPitch(float[] audioBuffer)
        {
            if (audioBuffer == null || audioBuffer.Length < _bufferSize)
            {
                // バッファサイズが不足している場合は処理しない
                return -1;
            }

            int tauEstimate = -1;
            float pitchInHz = -1;
            
            // ステップ1: 差分関数 (Difference Function)
            Difference(audioBuffer);
            
            // ステップ2: 累積平均正規化差分関数 (Cumulative Mean Normalized Difference Function)
            CumulativeMeanNormalizedDifference();
            
            // ステップ3: 絶対閾値 (Absolute Threshold)
            tauEstimate = AbsoluteThreshold();
            
            // 周期が見つかった場合
            if (tauEstimate != -1)
            {
                // ステップ4: 放物線補間 (Parabolic Interpolation)
                float betterTau = ParabolicInterpolation(tauEstimate);
                
                // ピッチ変換
                pitchInHz = _sampleRate / betterTau;
            }
            
            return pitchInHz;
        }
        
        /// <summary>
        /// ステップ1: 差分関数の計算
        /// d_t(tau) = sum((x[j] - x[j+tau])^2)
        /// </summary>
        private void Difference(float[] buffer)
        {
            int halfSize = _bufferSize / 2;
            
            for (int tau = 0; tau < halfSize; tau++)
            {
                _yinBuffer[tau] = 0;
            }
            
            for (int tau = 1; tau < halfSize; tau++)
            {
                for (int j = 0; j < halfSize; j++)
                {
                    float delta = buffer[j] - buffer[j + tau];
                    _yinBuffer[tau] += delta * delta;
                }
            }
        }
        
        /// <summary>
        /// ステップ2: 累積平均正規化差分関数の計算
        /// d'_t(tau) = d_t(tau) / ((1/tau) * sum(d_t(j)))
        /// </summary>
        private void CumulativeMeanNormalizedDifference()
        {
            int halfSize = _bufferSize / 2;
            _yinBuffer[0] = 1;
            float runningSum = 0;
            
            for (int tau = 1; tau < halfSize; tau++)
            {
                runningSum += _yinBuffer[tau];
                _yinBuffer[tau] *= tau / runningSum;
            }
        }
        
        /// <summary>
        /// ステップ3: 絶対閾値を用いて周期候補を探す
        /// </summary>
        private int AbsoluteThreshold()
        {
            int halfSize = _bufferSize / 2;
            
            // 閾値より小さい最初の谷を探す
            for (int tau = 2; tau < halfSize; tau++)
            {
                if (_yinBuffer[tau] < _threshold)
                {
                    while (tau + 1 < halfSize && _yinBuffer[tau + 1] < _yinBuffer[tau])
                    {
                        tau++;
                    }
                    return tau;
                }
            }
            
            // 閾値以下の谷がない場合、大域的最小値を探す（オプション）
            // ここでは検出失敗とするか、最も深い谷を返す実装も可能
            // 安定性のため、閾値を超えた場合は検出なしとする
            return -1;
        }
        
        /// <summary>
        /// ステップ4: 放物線補間による高精度化
        /// </summary>
        private float ParabolicInterpolation(int tauEstimate)
        {
            if (tauEstimate <= 0 || tauEstimate >= _yinBuffer.Length - 1)
            {
                return tauEstimate;
            }
            
            float s0 = _yinBuffer[tauEstimate - 1];
            float s1 = _yinBuffer[tauEstimate];
            float s2 = _yinBuffer[tauEstimate + 1];
            
            float adjustment = (s2 - s0) / (2 * (2 * s1 - s2 - s0));
            return tauEstimate + adjustment;
        }
    }
}

