package mathutil

import "math"

// Vector2 là vector 2D dùng chung giữa các package server-side.
type Vector2 struct {
	X, Y float32
}

func (v Vector2) Add(other Vector2) Vector2 {
	return Vector2{v.X + other.X, v.Y + other.Y}
}

func (v Vector2) Sub(other Vector2) Vector2 {
	return Vector2{v.X - other.X, v.Y - other.Y}
}

func (v Vector2) Scale(s float32) Vector2 {
	return Vector2{v.X * s, v.Y * s}
}

func (v Vector2) LengthSq() float32 {
	return v.X*v.X + v.Y*v.Y
}

func (v Vector2) Length() float32 {
	return float32(math.Sqrt(float64(v.LengthSq())))
}

func (v Vector2) Normalized() Vector2 {
	l := v.Length()
	if l < 1e-6 {
		return Vector2{}
	}
	return Vector2{v.X / l, v.Y / l}
}

func (v Vector2) Distance(other Vector2) float32 {
	return v.Sub(other).Length()
}
