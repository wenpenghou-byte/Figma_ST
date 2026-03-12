@echo off
cd /d C:\Users\houwenpeng\Desktop\FigmaSearch

git config --global user.name "wenpenghou-byte"
git config --global user.email "wenpenghou@gmail.com"

git add .
git commit -m "Full source: FigmaSearch v1.0.0"
git branch -M main
git push -u origin main --force

git tag -f v1.0.0
git push origin v1.0.0 --force

echo.
echo Done! Check GitHub Actions for build status.
pause
