# UDP 音频包格式（v1）

头部（12 字节）
----------------

```
0..3   magic         0x4F505553 ("OPUS")
4..5   seq           uint16 (循环)
6..9   timestamp48k  uint32 (以 48k 递增)
10     flags         uint8  (bit0..)
11     keyId         uint8
```

负载
----

- Opus 压缩帧（20 ms），建议 64–160 kbps 可调

加密（AES-256-GCM）
-------------------

- 关联数据（AAD）：头部 12 字节
- Nonce（12B）：`salt(4B) || seq(2B) || timestamp48k(4B) || flags(1B) || keyId(1B)`
- 标签（Tag）：16B 附加在密文尾部

重放与乱序
----------

- 接收端维护 2^12 滑动窗口，重复与越窗丢弃
- 抖动缓冲建议 60–80 ms 目标深度

示例
----

```
magic=OPUS, seq=1234, ts48k=96000, flags=0x01, keyId=0x00
payload=OpusFrame(52 bytes) + GCM Tag(16 bytes)
```


