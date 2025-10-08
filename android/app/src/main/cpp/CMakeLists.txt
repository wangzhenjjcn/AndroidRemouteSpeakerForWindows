cmake_minimum_required(VERSION 3.22.1)
project(opusbridge)

include(FetchContent)
set(OPUS_BUILD_SHARED_LIBRARY OFF CACHE BOOL "" FORCE)
set(OPUS_BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(BUILD_TESTING OFF CACHE BOOL "" FORCE)
FetchContent_Declare(opus
  URL https://github.com/xiph/opus/archive/refs/tags/v1.5.2.tar.gz
)
FetchContent_MakeAvailable(opus)

add_library(opusbridge SHARED
  opus_bridge.cpp
)

target_include_directories(opusbridge PRIVATE ${opus_SOURCE_DIR}/include)

target_link_libraries(opusbridge
  opus
  android
  log
)


