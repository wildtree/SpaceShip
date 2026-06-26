cmake_minimum_required(VERSION 3.16)
project(SpaceShip C)

set(CMAKE_C_STANDARD 11)

find_package(SDL3 REQUIRED)
find_package(OpenGL REQUIRED)

add_executable(spaceship src/main.c)
target_link_libraries(spaceship SDL3::SDL3 OpenGL::GL m)
target_compile_options(spaceship PRIVATE -O2 -Wall)
