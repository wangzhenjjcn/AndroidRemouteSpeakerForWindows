#include <jni.h>
#include <android/log.h>
#include <opus.h>
#include <vector>

#define LOG_TAG "OpusBridge"
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

struct DecoderCtx {
  OpusDecoder* dec {nullptr};
  int sampleRate {48000};
  int channels {2};
};

extern "C" JNIEXPORT jlong JNICALL
Java_com_audiobridge_app_codec_OpusBridge_nativeCreateDecoder(JNIEnv*, jobject, jint sampleRate, jint channels) {
  int err = 0;
  if (channels != 1 && channels != 2) channels = 2;
  OpusDecoder* dec = opus_decoder_create(sampleRate, channels, &err);
  if (err != OPUS_OK || !dec) {
    LOGE("opus_decoder_create failed: %d", err);
    return 0;
  }
  auto* ctx = new DecoderCtx();
  ctx->dec = dec;
  ctx->sampleRate = sampleRate;
  ctx->channels = channels;
  return reinterpret_cast<jlong>(ctx);
}

extern "C" JNIEXPORT void JNICALL
Java_com_audiobridge_app_codec_OpusBridge_nativeDestroyDecoder(JNIEnv*, jobject, jlong handle) {
  auto* ctx = reinterpret_cast<DecoderCtx*>(handle);
  if (!ctx) return;
  if (ctx->dec) opus_decoder_destroy(ctx->dec);
  delete ctx;
}

extern "C" JNIEXPORT jint JNICALL
Java_com_audiobridge_app_codec_OpusBridge_nativeDecode(JNIEnv* env, jobject, jlong handle, jbyteArray inBytes, jint inLen, jshortArray outPcm) {
  auto* ctx = reinterpret_cast<DecoderCtx*>(handle);
  if (!ctx || !ctx->dec) return 0;

  jboolean isCopy = JNI_FALSE;
  jbyte* inPtr = env->GetByteArrayElements(inBytes, &isCopy);
  jshort* outPtr = env->GetShortArrayElements(outPcm, nullptr);
  if (!inPtr || !outPtr) {
    if (inPtr) env->ReleaseByteArrayElements(inBytes, inPtr, JNI_ABORT);
    if (outPtr) env->ReleaseShortArrayElements(outPcm, outPtr, 0);
    return 0;
  }
  // 假设 outPcm 提供足够空间（960*channels for 20ms@48k）
  int frameSamples = opus_decode(ctx->dec, reinterpret_cast<const unsigned char*>(inPtr), inLen, outPtr, 960, 0);
  env->ReleaseByteArrayElements(inBytes, inPtr, JNI_ABORT);
  env->ReleaseShortArrayElements(outPcm, outPtr, 0);
  if (frameSamples < 0) {
    LOGE("opus_decode failed: %d", frameSamples);
    return 0;
  }
  return frameSamples * ctx->channels;
}


