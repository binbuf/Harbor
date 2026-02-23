

- Dock
  - The dock is currently reserving too much space on the bottom. It should be about 3x closer to the bottom then it currently is (e.g. if it's 100px from bottom it should be 30px).
  - Magnification setting is overclipping the iconand the top can't be see now as the icon grows beyond the background but then is visually clipped.
  - Long holding left click should bring up the context menu like short right clicking
  - Magnification only seems to work on trash icon and now all icons. It also doesn't zoom up the neighbors like the real MacOS dock effect does.
- Dock & Top Menu
  - 
- Top Menu
  - 
- Settings
  - Dock auto-hide doesn't work right. "When overlapped" just hides the dock and it doesn't come up regardless if there is an app in the reservation area or not. "Always" never brings the dock up if your mouse is by it.
  - Do we need to remove explorer.exe or represent the option? I think our design was updated to use explorer instead of removing it. Deeply review the changes to the codebase and also update docs/Design.md as needed.
  -Icon size doesn't appear to actually work
