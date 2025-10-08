# Distributed under the OSI-approved BSD 3-Clause License.  See accompanying
# file Copyright.txt or https://cmake.org/licensing for details.

cmake_minimum_required(VERSION 3.22.1-g37088a8-dirty)

# We name the project and the target for the ExternalProject_Add() call
# to something that will highlight to the user what we are working on if
# something goes wrong and an error message is produced.

project(opus-populate NONE)



include(ExternalProject)
ExternalProject_Add(opus-populate
                     "UPDATE_DISCONNECTED" "False" "URL" "https://github.com/xiph/opus/archive/refs/tags/v1.5.2.tar.gz"
                    SOURCE_DIR          "C:/Users/WangZhen/Documents/GitHub/AndroidSpeaker/android/app/.cxx/Debug/312b4m23/arm64-v8a/_deps/opus-src"
                    BINARY_DIR          "C:/Users/WangZhen/Documents/GitHub/AndroidSpeaker/android/app/.cxx/Debug/312b4m23/arm64-v8a/_deps/opus-build"
                    CONFIGURE_COMMAND   ""
                    BUILD_COMMAND       ""
                    INSTALL_COMMAND     ""
                    TEST_COMMAND        ""
                    USES_TERMINAL_DOWNLOAD  YES
                    USES_TERMINAL_UPDATE    YES
)


