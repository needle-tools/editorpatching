# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.1.0-pre.1] - 2021-03-20
- expose option to not set persistent patch state to avoid writing dynamic patches state information in settings file

## [1.1.0-pre] - 2021-03-17
- enable patches immediately
- added CanEnable to EditorPatch for delaying apply patch to allow for waiting of other requirements, e.g. when GUI is not yet loaded
- ``PatchManager.EnablePatch`` awaitable
- support Harmony.Debug and added built in patch to log harmony output to Unity console
- allow to update already registered ``EditorPatchProvider`` instance
- allow EditorPatchProvider to override ``Id``.
- EditorPatchProvider ``DisplayName`` and ``Description`` are now optional 
- updated Harmony plugin to 2.0.4

## [1.0.1] - 2021-03-01
- license guid conflict fix

## [1.0.0] - 2021-02-28
- initial public release
