package main

import (
	"fmt"
	"adventure2d-server/internal/mapdata"
)

func main() {
	md, err := mapdata.LoadMap(`d:\Aventure\Adventure-2D\Assets\MapData\Map1.mapdata`)
	if err != nil {
		fmt.Println("Error:", err)
		return
	}
	fmt.Printf("Map: %s (ID=%d)\n", md.Name, md.MapID)
	fmt.Printf("Spawns (%d):\n", len(md.Spawns))
	for i, s := range md.Spawns {
		fmt.Printf("  [%d] X=%.2f Y=%.2f\n", i, s.X, s.Y)
	}
	if md.Tiles != nil {
		t := md.Tiles
		fmt.Printf("Tiles: OriginX=%d OriginY=%d Width=%d Height=%d\n", t.OriginX, t.OriginY, t.Width, t.Height)
		fmt.Printf("Map world bounds: X[%d .. %d]  Y[%d .. %d]\n",
			t.OriginX, int(t.OriginX)+int(t.Width),
			t.OriginY, int(t.OriginY)+int(t.Height))
	}
}
