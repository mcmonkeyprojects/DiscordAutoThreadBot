#!/bin/bash
git pull origin master
git submodule update --init --recursive
dotnet build DiscordAutoThreadBot.sln --configuration Release -o ./bin/live_release
dotnet bin/live_release/DiscordAutoThreadBot.dll $1
