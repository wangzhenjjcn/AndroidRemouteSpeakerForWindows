using System;

namespace AudioBridge.Windows.Audio
{
  /// <summary>
  /// 简单的线性插值音频重采样器
  /// 用于将任意采样率转换为目标采样率
  /// </summary>
  public sealed class AudioResampler
  {
    private readonly int _sourceSampleRate;
    private readonly int _targetSampleRate;
    private readonly int _channels;
    private double _position = 0.0;
    
    public AudioResampler(int sourceSampleRate, int targetSampleRate, int channels)
    {
      _sourceSampleRate = sourceSampleRate;
      _targetSampleRate = targetSampleRate;
      _channels = channels;
    }
    
    /// <summary>
    /// 重采样音频数据（交错格式）
    /// </summary>
    /// <param name="input">输入音频（交错格式）</param>
    /// <param name="output">输出缓冲区</param>
    /// <returns>输出的样本数</returns>
    public int Resample(ReadOnlySpan<float> input, Span<float> output)
    {
      if (_sourceSampleRate == _targetSampleRate)
      {
        // 无需重采样，直接复制
        int copyCount = Math.Min(input.Length, output.Length);
        input.Slice(0, copyCount).CopyTo(output);
        return copyCount;
      }
      
      int inputFrames = input.Length / _channels;
      int outputFrames = output.Length / _channels;
      double ratio = (double)_sourceSampleRate / _targetSampleRate;
      
      int outputIndex = 0;
      
      for (int outFrame = 0; outFrame < outputFrames; outFrame++)
      {
        // 计算在输入中的位置
        double srcPos = outFrame * ratio;
        int srcIndex = (int)srcPos;
        double frac = srcPos - srcIndex;
        
        // 边界检查
        if (srcIndex >= inputFrames - 1)
        {
          break;
        }
        
        // 对每个声道进行线性插值
        for (int ch = 0; ch < _channels; ch++)
        {
          int idx0 = srcIndex * _channels + ch;
          int idx1 = (srcIndex + 1) * _channels + ch;
          
          if (idx1 < input.Length)
          {
            float sample0 = input[idx0];
            float sample1 = input[idx1];
            float interpolated = sample0 + (float)frac * (sample1 - sample0);
            output[outputIndex++] = interpolated;
          }
          else
          {
            output[outputIndex++] = input[idx0];
          }
        }
      }
      
      return outputIndex;
    }
    
    /// <summary>
    /// 计算重采样后的输出样本数
    /// </summary>
    public int GetOutputSampleCount(int inputSampleCount)
    {
      if (_sourceSampleRate == _targetSampleRate)
        return inputSampleCount;
        
      int inputFrames = inputSampleCount / _channels;
      int outputFrames = (int)(inputFrames * (double)_targetSampleRate / _sourceSampleRate);
      return outputFrames * _channels;
    }
  }
}


